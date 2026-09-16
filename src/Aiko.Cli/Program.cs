using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

var command = args.Length == 0 ? "help" : args[0];

var exitCode = command switch
{
    "init" => await InitAsync(args),
    "serve" => await ServeAsync(),
    "ui" => await UiAsync(),
    "status" => await StatusAsync(),
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
          serve                                         Start the Aiko daemon
          ui                                            Open the UI in the browser
          status                                        Daemon, data and port status
          agent list                                    List agents and detected installs
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

    var project = await initializer.InitializeAsync(
        new InitializeProjectRequest(path, ReadOption(args, "--name"), policy),
        CancellationToken.None);
    Console.WriteLine($"Registered project {project.Id} at {project.RootPath}");
    return 0;
}

static async Task<int> ServeAsync()
{
    var serverDll = Path.Combine(AppContext.BaseDirectory, "Aiko.Server.dll");
    if (!File.Exists(serverDll))
    {
        Console.Error.WriteLine(
            $"Aiko.Server.dll was not found next to the aiko CLI ({AppContext.BaseDirectory}). Run the installer first.");
        return 1;
    }

    var startInfo = new ProcessStartInfo("dotnet", $"\"{serverDll}\"")
    {
        UseShellExecute = false
    };
    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start the Aiko daemon.");
    Console.WriteLine($"Started Aiko daemon (pid {process.Id}). Press Ctrl+C to stop.");
    await process.WaitForExitAsync();
    return process.ExitCode;
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
