using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

var command = args.Length == 0 ? "help" : args[0];

var exitCode = command switch
{
    "init" => await InitAsync(args),
    "project" => await ProjectAsync(args),
    "serve" => await ServeAsync(),
    "ui" => await UiAsync(),
    "status" => await StatusAsync(),
    "doctor" => await DoctorAsync(args),
    "repair" => await RepairAsync(args),
    "agent" => await AgentAsync(args),
    "token" => await TokenAsync(),
    "reindex" => await ReindexAsync(args),
    "help" or "--help" or "-h" => Help(),
    _ => Unknown(command)
};
return exitCode;

static int Help()
{
    Console.WriteLine(
        """
        Aiko - local AI development orchestrator.

        Usage: aiko <command> [options]

        Commands:
          init <path> [--name <n>] [--git-policy <p>]   Register a project (.aiko)
          project remove <id> [--yes]                   Unregister a project (keeps its files)
          serve                                         Start the Aiko daemon
          ui                                            Open the UI in the browser
          status                                        Daemon, data and port status
          doctor [--project <id>]                       Check the installation and report (changes nothing)
          repair [--fix] [--project <id>] [--agent <ids>]
                                                        Reindex and rewrite stale agent configs
          agent list                                    List agents and detected installs
          agent install --project <id> [--agent <ids>] [--scope user]
                                                        Connect an agent to a project
          agent uninstall --project <id> [--agent <ids>] [--scope user]
                                                        Disconnect an agent from a project
          token show                                    Print the local access token
          reindex <projectId>                           Rebuild a project's SQLite projections

        Git policies: local-only (default), track-project-knowledge, custom.
        """);
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 2;
}

static async Task<int> InitAsync(string[] args)
{
    var path = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine("Usage: aiko init <path> [--name <n>] [--git-policy <p>]");
        return 2;
    }

    var policy = ParseGitPolicy(ReadOption(args, "--git-policy"));
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var reindexer = new ProjectReindexer(catalog, database);
    var initializer = new ProjectInitializer(catalog, reindexer, new FileAppSettingsStore(dataPaths, catalog));

    try
    {
        var project = await initializer.InitializeAsync(
            new InitializeProjectRequest(path, ReadOption(args, "--name"), policy),
            CancellationToken.None);
        Console.WriteLine($"Registered project {project.Id} at {project.RootPath}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or ArgumentException or InvalidOperationException)
    {
        // A path that does not exist, a duplicate registration or an unreadable directory is a user
        // mistake, not a crash: report it the way the usage branch above does, without a stack trace.
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

// `aiko project remove <id>` unregisters a project. Only the catalog entry (and the SQLite projections
// that hang off it) goes: the project's files - including .aiko - belong to the user, and a mistyped
// `aiko init` path is exactly the case this command exists to undo.
static async Task<int> ProjectAsync(string[] args)
{
    var subcommand = args.Length > 1 ? args[1] : null;
    var projectId = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : null;
    if (!string.Equals(subcommand, "remove", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("Usage: aiko project remove <projectId> [--yes]");
        return 2;
    }

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);

    var project = await catalog.FindAsync(projectId, CancellationToken.None);
    if (project is null)
    {
        Console.Error.WriteLine($"No registered project with id {projectId}.");
        return 1;
    }

    if (!HasFlag(args, "--yes", "-y") &&
        !Confirm($"Unregister {project.Name} ({project.RootPath})? Its files will be kept"))
    {
        Console.Error.WriteLine("Cancelled. Nothing changed.");
        return 1;
    }

    await catalog.RemoveAsync(projectId, CancellationToken.None);
    Console.WriteLine(
        $"Unregistered project {project.Id} ({project.RootPath}). Files on disk were not touched.");
    return 0;
}

// Unregistering changes what the daemon shows, so it asks first. A redirected stdin means a script:
// there the explicit `--yes` is the only consent that counts, and waiting on a prompt nobody can
// answer would hang the caller.
static bool Confirm(string question)
{
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine("Refusing to continue: pass --yes to confirm in a non-interactive shell.");
        return false;
    }

    Console.Write($"{question} [y/N] ");
    var answer = Console.ReadLine();
    return answer is not null && answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
}

static bool HasFlag(string[] args, params string[] flags) =>
    args.Any(arg => flags.Any(flag => string.Equals(arg, flag, StringComparison.Ordinal)));

static async Task<int> ServeAsync()
{

    var server = ResolveServerCommand();
    if (server is null)
    {
        Console.Error.WriteLine(
            $"The Aiko daemon was not found next to the aiko CLI ({AppContext.BaseDirectory}). Run the installer first.");
        return 1;
    }

    var startInfo = new ProcessStartInfo(server.Value.FileName, server.Value.Arguments)
    {
        UseShellExecute = false
    };
    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start the Aiko daemon.");
    Console.WriteLine($"Started Aiko daemon (pid {process.Id}). Press Ctrl+C to stop.");
    await process.WaitForExitAsync();
    return process.ExitCode;
}

// The release layout ships the daemon as a self-contained executable (Aiko.Server.exe) either next
// to the CLI or in a `server` subdirectory. A source build publishes it framework-dependent, and
// then the daemon is a managed assembly started through `dotnet`.
static (string FileName, string Arguments)? ResolveServerCommand()
{
    var directories = new[]
    {
        AppContext.BaseDirectory,
        Path.Combine(AppContext.BaseDirectory, "server")
    };

    foreach (var directory in directories)
    {
        var executable = Path.Combine(directory, "Aiko.Server.exe");
        if (File.Exists(executable))
        {
            return (executable, string.Empty);
        }

        var assembly = Path.Combine(directory, "Aiko.Server.dll");
        if (File.Exists(assembly))
        {
            return ("dotnet", $"\"{assembly}\"");
        }
    }

    return null;
}

static async Task<int> UiAsync()
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    if (settings is null)
    {
        Console.Error.WriteLine("Start the daemon once first, so the port is known.");
        return 1;
    }

    var token = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.Port}") };
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pair-request");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    var code = document.RootElement.GetProperty("code").GetString()
        ?? throw new InvalidOperationException("The daemon returned no pairing code.");

    var url = $"http://127.0.0.1:{settings.Port}/#pair={Uri.EscapeDataString(code)}";
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    Console.WriteLine($"Opened {url}");
    return 0;
}

static async Task<int> StatusAsync()
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    Console.WriteLine($"Data directory: {Path.GetDirectoryName(dataPaths.DatabasePath)}");
    Console.WriteLine($"Database:        {dataPaths.DatabasePath}");

    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    if (settings is null)
    {
        Console.WriteLine("Daemon:          not started yet (no saved port)");
        return 0;
    }

    Console.WriteLine($"Port:            {settings.Port}");
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.Port}"), Timeout = TimeSpan.FromSeconds(2) };
    try
    {
        using var health = await http.GetAsync("/health");
        Console.WriteLine($"Daemon:          {(health.IsSuccessStatusCode ? "running (healthy)" : $"responding {health.StatusCode}")}");
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("Daemon:          not running");
    }
    catch (OperationCanceledException)
    {
        // A daemon that does not answer within the timeout is stopped as far as the user is concerned;
        // an escaping TaskCanceledException here prints a stack trace instead of saying so.
        Console.WriteLine("Daemon:          not running (no answer)");
    }

    return 0;
}

static async Task<int> AgentAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: aiko agent list | install --project <id> [--agent <ids>] | uninstall --project <id> [--agent <ids>]");
        return 2;
    }

    return args[1] switch
    {
        "list" => await AgentListAsync(),
        "install" => await AgentInstallAsync(args, uninstall: false),
        "uninstall" => await AgentInstallAsync(args, uninstall: true),
        _ => Unknown($"agent {args[1]}")
    };
}

static async Task<int> AgentListAsync()
{
    foreach (var adapter in CreateAdapters())
    {
        var installations = await adapter.DetectInstallationsAsync(CancellationToken.None);
        Console.WriteLine(
            $"{adapter.DisplayName} ({adapter.Id}): " +
            (installations.Count == 0 ? "not found" : string.Join(", ", installations.Select(i => i.ExecutablePath))));
    }

    return 0;
}

static async Task<int> AgentInstallAsync(string[] args, bool uninstall)
{
    var selected = ParseSelected(args);
    var scope = ReadOption(args, "--scope");

    if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var adapter in CreateAdapters().Where(a => selected.Contains(a.Id)))
        {
            var result = uninstall
                ? await adapter.UninstallUserAsync(CancellationToken.None)
                : await adapter.ApplyUserInstallAsync(CancellationToken.None);
            Console.WriteLine($"{adapter.Id}: {(result.Succeeded ? (uninstall ? "uninstalled" : "installed") : "failed")}");
            foreach (var file in result.Files)
            {
                Console.WriteLine($"  {file.Status} {file.Path}");
            }
        }

        return 0;
    }

    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine($"Usage: aiko agent {(uninstall ? "uninstall" : "install")} --project <id> [--agent <ids>] [--scope user]");
        return 2;
    }

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog);

    if (uninstall)
    {
        var result = await installer.UninstallAsync(projectId, selected, CancellationToken.None);
        foreach (var item in result.AdapterResults)
        {
            Console.WriteLine($"{item.AdapterId}: {(item.Succeeded ? "uninstalled" : "failed")}");
            foreach (var file in item.Files)
            {
                Console.WriteLine($"  {file.Status} {file.Path}");
            }
        }

        PrintUnknown(result.UnknownAdapterIds);
        return 0;
    }

    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    if (settings is null)
    {
        Console.Error.WriteLine("Start the daemon once first, so the MCP endpoint port is known.");
        return 1;
    }

    var endpoint = $"http://127.0.0.1:{settings.Port}/mcp/projects/{projectId}";
    var applied = await installer.ApplyAsync(projectId, endpoint, selected, CancellationToken.None);
    foreach (var item in applied.AdapterResults)
    {
        Console.WriteLine($"{item.AdapterId}: {(item.Succeeded ? "installed" : "failed")}");
        foreach (var file in item.Files)
        {
            Console.WriteLine($"  {file.Status} {file.Path}");
        }

        foreach (var warning in item.Warnings)
        {
            Console.WriteLine($"  warning: {warning}");
        }
    }

    PrintUnknown(applied.UnknownAdapterIds);
    return 0;
}

static string[] ParseSelected(string[] args)
{
    var selected = (ReadOption(args, "--agent") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return selected.Length == 0 ? ["claude-code", "codex", "cursor", "zcode"] : selected;
}

static void PrintUnknown(IReadOnlyList<string> unknown)
{
    foreach (var id in unknown)
    {
        Console.WriteLine($"unknown adapter: {id}");
    }
}

static IAgentAdapter[] CreateAdapters() =>
[
    new ClaudeCodeAgentAdapter(),
    new CodexAgentAdapter(),
    new CursorAgentAdapter(),
    new ZCodeAgentAdapter()
];

static async Task<int> TokenAsync()
{
    var store = new AccessTokenStore(AikoDataPaths.FromEnvironment());
    Console.WriteLine(await store.GetOrCreateAsync());
    return 0;
}

// `aiko doctor` reports on the installation without changing it. Every finding names its fix, and the
// exit code stays 0: a diagnosis that ran is a success even when it found problems.
static async Task<int> DoctorAsync(string[] args)
{
    PrintFindings(await InspectAsync(ReadOption(args, "--project")));
    return 0;
}

// `aiko repair` performs exactly what `aiko doctor` names. Without --fix it only reports, so the half
// that rewrites files is always an explicit choice, and it prints the report again afterwards so the
// result is visible rather than assumed.
static async Task<int> RepairAsync(string[] args)
{
    var projectId = ReadOption(args, "--project");
    var findings = await InspectAsync(projectId);
    PrintFindings(findings);
    if (!HasFlag(args, "--fix"))
    {
        if (findings.HasProblems)
        {
            Console.WriteLine();
            Console.WriteLine("Run `aiko repair --fix` to apply the fixes named above.");
        }

        return 0;
    }

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var reindexer = new ProjectReindexer(catalog, database);
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog);
    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();

    var registered = await catalog.ListAsync(CancellationToken.None);
    if (projectId is { Length: > 0 } requested)
    {
        registered = registered
            .Where(project => StringComparer.Ordinal.Equals(project.Id, requested))
            .ToArray();
    }

    var requestedAdapters = (ReadOption(args, "--agent") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var installedAdapters = requestedAdapters.Length > 0
        ? requestedAdapters
        : (await installer.DiscoverAsync(CancellationToken.None))
            .Where(adapter => adapter.Installations.Count > 0)
            .Select(adapter => adapter.Id)
            .ToArray();

    Console.WriteLine();
    foreach (var project in registered)
    {
        if (!Directory.Exists(AikoProjectPaths.DataRoot(project.RootPath)))
        {
            Console.WriteLine($"{project.Name}: skipped, no .aiko directory to rebuild from.");
            continue;
        }

        var reindexed = await reindexer.ReindexAsync(project.Id, CancellationToken.None);
        Console.WriteLine(
            $"Reindexed {project.Name}: {reindexed.Cards} cards, {reindexed.Relations} relations, " +
            $"{reindexed.MemoryDocuments} memory documents.");

        if (settings is null || installedAdapters.Length == 0)
        {
            continue;
        }

        var endpoint = $"http://127.0.0.1:{settings.Port}/mcp/projects/{project.Id}";
        var applied = await installer.ApplyAsync(
            project.Id,
            endpoint,
            installedAdapters,
            CancellationToken.None);
        foreach (var item in applied.AdapterResults)
        {
            var changed = item.Files.Count(file => file.Status is not InstallationFileStatus.Unchanged);
            Console.WriteLine(
                $"{project.Name}: {item.AdapterId} {(item.Succeeded ? "repaired" : "failed")}, " +
                $"{changed} file(s) written.");
        }
    }

    // The user-scope configurations carry the daemon endpoint too, so a port change leaves them stale
    // everywhere, not only inside the projects.
    if (settings is not null)
    {
        foreach (var adapter in CreateAdapters())
        {
            if ((await adapter.DetectInstallationsAsync(CancellationToken.None)).Count == 0)
            {
                continue;
            }

            var applied = await adapter.ApplyUserInstallAsync(CancellationToken.None);
            var changed = applied.Files.Count(file => file.Status is not InstallationFileStatus.Unchanged);
            Console.WriteLine(
                $"user scope: {adapter.Id} {(applied.Succeeded ? "repaired" : "failed")}, " +
                $"{changed} file(s) written.");
        }
    }

    Console.WriteLine();
    Console.WriteLine("After the repair:");
    PrintFindings(await InspectAsync(projectId));
    return 0;
}
// Wires the same services the daemon uses, locally: doctor and repair work with the daemon stopped,
// which is the state a broken installation is usually in.
static async Task<WorkshopDiagnostics> InspectAsync(string? projectId)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog);
    var diagnostics = new WorkshopDoctor(
        dataPaths,
        catalog,
        installer,
        CreateAdapters(),
        new DaemonEndpointConfiguration(dataPaths));
    return await diagnostics.InspectAsync(projectId, CancellationToken.None);
}

static void PrintFindings(WorkshopDiagnostics diagnostics)
{
    foreach (var finding in diagnostics.Findings)
    {
        var marker = finding.Severity switch
        {
            DiagnosticSeverity.Error => "error  ",
            DiagnosticSeverity.Warning => "warning",
            _ => "ok     "
        };
        Console.WriteLine($"{marker} {finding.Summary}");
        if (!string.IsNullOrWhiteSpace(finding.Detail))
        {
            Console.WriteLine($"          {finding.Detail}");
        }
    }

    Console.WriteLine(diagnostics.HasProblems
        ? "Problems found."
        : "Everything looks healthy.");
}

static async Task<int> ReindexAsync(string[] args)
{
    var projectId = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("Usage: aiko reindex <projectId>");
        return 2;
    }

    var database = new AikoDatabase(AikoDataPaths.FromEnvironment());
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var reindexer = new ProjectReindexer(catalog, database);
    var result = await reindexer.ReindexAsync(projectId, CancellationToken.None);
    Console.WriteLine(
        $"Reindexed project {projectId}: {result.Cards} cards, {result.Relations} relations, {result.MemoryDocuments} memory documents.");
    return 0;
}

static string? ReadOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static ProjectGitPolicy ParseGitPolicy(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? ProjectGitPolicy.LocalOnly
        : value.Trim().ToLowerInvariant() switch
        {
            "local-only" => ProjectGitPolicy.LocalOnly,
            "track-project-knowledge" => ProjectGitPolicy.TrackProjectKnowledge,
            "custom" => ProjectGitPolicy.Custom,
            _ => throw new ArgumentException(
                "Git policy must be local-only, track-project-knowledge or custom.")
        };
