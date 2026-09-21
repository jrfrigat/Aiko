using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Cli;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Commands;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Execution;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

var command = args.Length == 0 ? "help" : args[0];

var exitCode = command switch
{
    "init" => await InitAsync(args),
    "templates" => await TemplatesAsync(),
    "project" => await ProjectAsync(args),
    "serve" => await ServeAsync(args),
    "ui" => await UiAsync(),
    "status" => await StatusAsync(),
    "doctor" => await DoctorAsync(args),
    "repair" => await RepairAsync(args),
    "agent" => await AgentAsync(args),
    "token" => await TokenAsync(),
    "reindex" => await ReindexAsync(args),
    "commands" => await CommandsAsync(args),
    "uninstall" => await UninstallAsync(args),
    "autostart" => Autostart(args),
    "settings" => await SettingsAsync(args),
    "workflow" => await WorkflowAsync(args),
    "backup" => await BackupAsync(args),
    "restore" => await RestoreAsync(args),
    "logs" => await LogsAsync(args),
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
          init <path> [--name <n>] [--id <slug>] [--git-policy <p>] [--template <id>]
                                                        Register a project (.aiko); --id is the readable
                                                        handle used in the UI's URLs
          templates                                     List the project templates to create from
          project find [path]                           Report which Aiko project owns a folder
          project remove <id> [--yes]                   Unregister a project (keeps its files)
          serve [-d|--detached] [--port <p>]              Start the Aiko daemon: in this terminal by
                                                        default, in the background with -d (its log goes
                                                        to the data directory)
          serve stop [--port <p>]                       Ask a running daemon to stop (the port it saved,
                                                        or one given with --port)
          ui                                            Open the UI in the browser, starting a background
                                                        daemon when none is running
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
          uninstall [--yes] [--remove-data] [--remove-project-data]
                                                        Remove the installation: stop the daemon, drop the
                                                        install directory from the user PATH and delete the
                                                        published binaries. The data directory and the
                                                        projects' .aiko are kept unless asked for by name
          autostart enable|disable|status               Start the daemon at sign-in, or stop doing so
          settings get|set --project <id> | --template <id> [--port <p>]
                                                        Read or write a project's own settings, or a
                                                        template's defaults. There is no installation-level
                                                        settings document: a project owns its copy from init
          workflow get|set --project <id> [--workflow <id>] [--port <p>]
                                                        Read the project's pipelines, or write one back.
                                                        Writing goes through the daemon, which refuses to
                                                        strand a card in a stage it removed
          backup --project <id> [--output <path>]       Archive the project's .aiko, in the format
                                                        `aiko_backup` writes
          restore --archive <path> --project <id> [--yes]
                                                        Put an archive back over the project. Restoring
                                                        overwrites files, so it asks first, and reprojects
                                                        the board afterwards
          logs [--lines <n>] [--project <id>] [--after <id>] [--port <p>]
                                                        The tail of the daemon's log, and with --project the
                                                        tail of that project's event journal
          reindex <projectId>                           Rebuild a project's SQLite projections
          commands [--project <id>] [--card <id>] [--state <s>]
                                                        Show the commands a screen placed for an agent;
                                                        --state is open (default), all, or one state

        Git policies: local-only (default), track-project-knowledge, custom.
        """);
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 2;
}

static async Task<int> TemplatesAsync()
{
    var store = new FileProjectTemplateStore(AikoDataPaths.FromEnvironment());
    var listed = await store.ListAsync(CancellationToken.None);
    if (listed.Count == 0)
    {
        // Reading is read-only: the default template is written by the first init, not by a listing.
        Console.WriteLine("No project templates yet.");
        Console.WriteLine("aiko init <path> creates a project from the built-in default template.");
        return 0;
    }

    foreach (var template in listed)
    {
        Console.WriteLine($"{(template.IsDefault ? "*" : " ")} {template.Id,-16} v{template.Version}  {template.Name}");
        if (!string.IsNullOrWhiteSpace(template.Description))
        {
            Console.WriteLine($"    {template.Description}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Pass --template <id> to aiko init to create a project from one of them.");
    return 0;
}

static async Task<int> InitAsync(string[] args)
{
    var path = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrWhiteSpace(path))
    {
        Console.Error.WriteLine(
            "Usage: aiko init <path> [--name <n>] [--id <slug>] [--git-policy <p>] [--template <id>] "
            + "[--agent <id>]");
        return 2;
    }

    // The agent that runs init names itself and is connected to the project in the same step. A person
    // creating a project does not: there the form asks which agents the project is for.
    var agentId = ReadOption(args, "--agent");

    var policy = ParseGitPolicy(ReadOption(args, "--git-policy"));
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var reindexer = new ProjectReindexer(catalog, database);
    var initializer = new ProjectInitializer(
        catalog,
        reindexer,
        new FileAppSettingsStore(catalog),
        new FileProjectTemplateStore(dataPaths));

    try
    {
        var project = await initializer.InitializeAsync(
            // --template picks what the project is created from; without it the default template is used.
            // --id is the readable handle the UI's URLs use; without it one is derived from the name and
            // made unique, so only a value the user typed can be refused.
            new InitializeProjectRequest(
                path,
                ReadOption(args, "--name"),
                policy,
                ReadOption(args, "--template"),
                ReadOption(args, "--id")),
            CancellationToken.None);
        Console.WriteLine($"Registered project {project.Handle} ({project.Id}) at {project.RootPath}");
        return string.IsNullOrWhiteSpace(agentId)
            ? 0
            : await ConnectAgentAsync(catalog, dataPaths, project, agentId);
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
    var argument = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : null;

    if (string.Equals(subcommand, "find", StringComparison.Ordinal))
    {
        return await FindProjectAsync(argument ?? Directory.GetCurrentDirectory());
    }

    if (!string.Equals(subcommand, "remove", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(argument))
    {
        Console.Error.WriteLine("Usage: aiko project find [path]");
        Console.Error.WriteLine("       aiko project remove <projectId> [--yes]");
        return 2;
    }

    var projectId = argument;
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

// Answers the one question a user-scope skill has to ask before it touches anything: which project is this
// folder? It prints what to do about each answer, not only the state, because the caller is an agent that
// has to tell the user what to run. Exit code 0 means a project was found, 1 means it was not.
static async ValueTask<int> FindProjectAsync(string path)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);

    var result = await ProjectPathLookup.ResolveAsync(catalog, path, CancellationToken.None);
    switch (result.Match)
    {
        case ProjectPathMatch.Registered when result.Project is { } project:
            Console.WriteLine($"project: {project.Handle}");
            Console.WriteLine($"id: {project.Id}");
            Console.WriteLine($"name: {project.Name}");
            Console.WriteLine($"root: {project.RootPath}");
            Console.WriteLine($"dir: {result.Path}");
            return 0;
        case ProjectPathMatch.Initialized:
            Console.WriteLine("project: none");
            Console.WriteLine($"dir: {result.Path}");
            Console.WriteLine("The folder carries .aiko, but the daemon has no project registered for it.");
            Console.WriteLine($"Register it with: aiko init \"{result.Path}\"");
            return 1;
        default:
            Console.WriteLine("project: none");
            Console.WriteLine($"dir: {result.Path}");
            Console.WriteLine("The folder is not an Aiko project: no .aiko here, nothing registered for it.");
            Console.WriteLine($"Create one with: aiko init \"{result.Path}\"");
            return 1;
    }
}

// Connects the agent that ran `aiko init` to the project it just created. Idempotent by design: a second
// agent running init on the same project only adds itself, and one that is already connected is told so
// rather than given an error.
static async ValueTask<int> ConnectAgentAsync(
    IProjectCatalog catalog,
    AikoDataPaths dataPaths,
    RegisteredProject project,
    string agentId)
{
    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    if (settings is null)
    {
        Console.Error.WriteLine("Start the daemon once first, so the MCP endpoint port is known.");
        return 1;
    }

    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog, new FileProjectDefinitionStore(catalog));
    var result = await installer.ApplyAsync(
        project.Id,
        ProjectMcpEndpoint.For($"http://127.0.0.1:{settings.Port}", project),
        await new AccessTokenStore(dataPaths).GetOrCreateAsync(),
        [agentId],
        CancellationToken.None);

    if (result.UnknownAdapterIds.Count > 0)
    {
        Console.Error.WriteLine($"Unknown agent: {agentId}");
        return 1;
    }

    var changed = result.AdapterResults
        .SelectMany(item => item.Files)
        .Count(file => file.Status is not InstallationFileStatus.Unchanged);
    Console.WriteLine(changed == 0
        ? $"Agent already connected to project {project.Handle}."
        : $"Connected {agentId} to project {project.Handle}: {changed} file(s) written.");
    return 0;
}

// Prints the commands a screen placed for an agent: what is waiting, what an agent has taken, and what
// happened. It reads the project's own file directly instead of going through the daemon, because a queue
// whose point is to survive until an agent runs has to be readable when nothing else is.
static async ValueTask<int> CommandsAsync(string[] args)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);

    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(projectId))
    {
        var resolved = await ProjectPathLookup.ResolveAsync(
            catalog,
            Directory.GetCurrentDirectory(),
            CancellationToken.None);
        if (resolved is not { Match: ProjectPathMatch.Registered, Project: { } current })
        {
            Console.Error.WriteLine(
                "This folder is not a registered Aiko project. Name one with --project <id>.");
            return 1;
        }

        projectId = current.Id;
    }

    var state = ReadOption(args, "--state");
    var openOnly = string.IsNullOrWhiteSpace(state) ||
                   string.Equals(state, "open", StringComparison.OrdinalIgnoreCase);
    var includeClosed = !openOnly;

    IReadOnlyList<CardCommand> entries;
    try
    {
        var commands = new FileCardCommandStore(catalog, new FileCardStore(catalog, database));
        entries = await commands.ListAsync(projectId, includeClosed, CancellationToken.None);
    }
    catch (FileNotFoundException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }

    if (!openOnly && !string.Equals(state, "all", StringComparison.OrdinalIgnoreCase))
    {
        var wanted = CardCommands.ParseState(state!);
        entries = [.. entries.Where(entry => entry.State == wanted)];
    }

    if (ReadOption(args, "--card") is { Length: > 0 } cardId)
    {
        entries = [.. entries.Where(entry =>
            string.Equals(entry.CardId, cardId, StringComparison.Ordinal))];
    }

    if (entries.Count == 0)
    {
        Console.WriteLine(openOnly ? "No commands are waiting." : "No commands match.");
        return 0;
    }

    foreach (var entry in entries)
    {
        Console.WriteLine(
            $"{entry.Id}  {entry.State,-9}  {entry.Action,-6}  {entry.CardId}  " +
            $"{Describe(entry)}  {entry.RequestedBy}  {entry.CreatedAtUtc:u}");
        if (!string.IsNullOrWhiteSpace(entry.Message))
        {
            Console.WriteLine($"    {entry.Message}");
        }
    }

    return 0;

    // What the command asks for, in the terms of the thing it names, so a person reads one line and knows
    // whether an agent could act on it at all.
    static string Describe(CardCommand entry) =>
        entry.Action is CardCommandAction.Start
            ? $"stage {entry.StageId}"
            : $"execution {entry.ExecutionId}";
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

// `aiko serve` runs the daemon in this terminal; `-d`/`--detached` leaves it running without this console;
// `aiko serve stop` asks a running one to stop. The stop goes through the daemon's own endpoint rather than
// to the process table, because the daemon owns its shutdown: it can finish what it is doing, and the caller
// is told the request was accepted.
static async Task<int> ServeAsync(string[] args)
{
    var rest = args.Skip(1).ToArray();
    if (rest.Any(argument => string.Equals(argument, "stop", StringComparison.OrdinalIgnoreCase)))
    {
        return await StopDaemonAsync(ReadOption(args, "--port"));
    }

    var unknown = rest.FirstOrDefault(argument => !IsServeFlag(argument));
    if (unknown is not null)
    {
        Console.Error.WriteLine(
            $"Unknown argument for `aiko serve`: {unknown}. Use `aiko serve [-d]` to start a daemon, or `aiko serve stop` to stop one.");
        return 2;
    }

    var server = ResolveServerCommand();
    if (server is null)
    {
        Console.Error.WriteLine(
            $"The Aiko daemon was not found next to the aiko CLI ({AppContext.BaseDirectory}). Run the installer first.");
        return 1;
    }

    // One daemon per installation is the design, so a second start is refused rather than left to fail on a
    // port that is already taken - and `aiko ui` reaches the same answer through EnsureDaemonAsync.
    if (await FindRunningPortAsync() is { } running)
    {
        Console.WriteLine($"An Aiko daemon is already running on port {running}.");
        Console.WriteLine("Stop it with `aiko serve stop`.");
        return 0;
    }

    if (!HasFlag(args, "-d", "--detached"))
    {
        return await StartForegroundAsync(server.Value);
    }

    if (await StartDetachedAsync(server.Value) is not { } started)
    {
        return 1;
    }

    PrintStartedInBackground(started);
    return 0;
}

// The flags `aiko serve` accepts when it starts a daemon rather than stopping one.
static bool IsServeFlag(string argument) =>
    string.Equals(argument, "-d", StringComparison.Ordinal) ||
    string.Equals(argument, "--detached", StringComparison.OrdinalIgnoreCase);

// Runs the daemon in this console: its log is right here and Ctrl+C stops it. That is also the weakness -
// whatever happens to this terminal happens to the daemon - which is what `-d` is for.
static async Task<int> StartForegroundAsync((string FileName, string Arguments) server)
{
    var startInfo = new ProcessStartInfo(server.FileName, server.Arguments)
    {
        UseShellExecute = false
    };
    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start the Aiko daemon.");
    Console.WriteLine(
        $"Started Aiko daemon (pid {process.Id}). Press Ctrl+C to stop it; `aiko serve -d` runs it in the background instead.");
    await process.WaitForExitAsync();
    return process.ExitCode;
}

// Starts the daemon with a console of its own that nobody sees (CREATE_NO_WINDOW) and a log file, so closing
// the terminal that started it - or the Ctrl+C that ends it - no longer takes the daemon with it. Returns the
// pid, the port and the log path once the daemon answers /health; null when it never did, with the tail of its
// log printed, because "started" that nothing can connect to is not an answer.
static async Task<(int Pid, int Port, string LogPath)?> StartDetachedAsync((string FileName, string Arguments) server)
{
    var logPath = ResolveLogPath();
    var startInfo = new ProcessStartInfo(server.FileName, server.Arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.EnvironmentVariables["AIKO_LOG_FILE"] = logPath;

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start the Aiko daemon.");
    if (await WaitForHealthAsync(TimeSpan.FromSeconds(30)) is { } port)
    {
        return (process.Id, port, logPath);
    }

    Console.Error.WriteLine($"The daemon (pid {process.Id}) did not answer on its port within 30 seconds.");
    PrintLogTail(logPath);
    return null;
}

static void PrintStartedInBackground((int Pid, int Port, string LogPath) started)
{
    Console.WriteLine($"Started Aiko daemon in the background (pid {started.Pid}, port {started.Port}).");
    Console.WriteLine($"Log:    {started.LogPath}");
    Console.WriteLine("Stop:   aiko serve stop");
}

// Stops the daemon through its own endpoint. The port is the one the daemon saved when it started, or the one
// named with --port - which is how a daemon started by hand is reached, since a port requested through
// AIKO_PORT is never written to the settings file.
static async Task<int> StopDaemonAsync(string? requestedPort)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var port = requestedPort;
    if (string.IsNullOrWhiteSpace(port))
    {
        var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
        if (settings is null)
        {
            Console.Error.WriteLine(
                "No daemon port is known yet: pass --port <p>, or run `aiko status` to see the saved one.");
            return 1;
        }

        port = settings.Port.ToString();
    }

    // AIKO_TOKEN, when set, is the token the daemon being stopped was started with, so it has to win over the
    // stored one: a daemon started that way would otherwise answer 401 to its own stop command.
    var accessToken = Environment.GetEnvironmentVariable("AIKO_TOKEN");
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        accessToken = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    }

    using var http = new HttpClient
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(5)
    };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    try
    {
        using var response = await http.PostAsync("/api/v1/system/shutdown", content: null);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.Error.WriteLine(
                $"The daemon on port {port} refused the access token. If it was started with AIKO_TOKEN, " +
                "run this command with the same value in the environment.");
            return 1;
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"The daemon on port {port} answered {(int)response.StatusCode} to the shutdown request.");
            return 1;
        }
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine($"No daemon is answering on port {port}.");
        Console.Error.WriteLine(
            "Run `aiko status` to see the saved port, or pass the port of a hand-started daemon with --port <p>.");
        return 1;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine($"The daemon on port {port} did not answer within 5 seconds.");
        return 1;
    }

    // The endpoint answers while the host is still winding down, so the port is polled until it really stops
    // answering: reporting "stopped" over a daemon that is still serving is a lie the next command exposes.
    for (var attempt = 0; attempt < 20; attempt++)
    {
        await Task.Delay(250);
        try
        {
            using var health = await http.GetAsync("/health");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"Daemon stopped (port {port}).");
            return 0;
        }
    }

    Console.Error.WriteLine(
        $"The daemon on port {port} accepted the stop but is still answering; it may be finishing a run.");
    return 1;
}

// Waits for a daemon to answer /health and returns the port it did, or null when none did in time.
static async Task<int?> WaitForHealthAsync(TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        await Task.Delay(250);
        if (await FindRunningPortAsync() is { } port)
        {
            return port;
        }
    }

    return null;
}

// The port of a daemon that answers: the one AIKO_PORT names, or the one saved when it started. A daemon
// started with an explicit port never writes the settings file, so the environment is read first.
static async Task<int?> FindRunningPortAsync()
{
    if (RequestedPort() is { } requested && await IsHealthyAsync(requested))
    {
        return requested;
    }

    if (await new DaemonEndpointConfiguration(AikoDataPaths.FromEnvironment()).TryReadAsync() is { } settings &&
        await IsHealthyAsync(settings.Port))
    {
        return settings.Port;
    }

    return null;
}

// The port AIKO_PORT names, or null when the environment does not name one.
static int? RequestedPort() =>
    int.TryParse(Environment.GetEnvironmentVariable("AIKO_PORT"), out var port) ? port : null;

static async Task<bool> IsHealthyAsync(int port)
{
    try
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        using var health = await http.GetAsync("/health");
        return health.IsSuccessStatusCode;
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        return false;
    }
}

// The port of a daemon that answers, starting one in the background when none does. Asking for a project or
// the UI is asking to see the board, so a missing daemon is something to fix here rather than something to
// report back: the same start path as `aiko serve -d` runs, and only a daemon that never answers is a failure.
static async Task<int?> EnsureDaemonAsync()
{
    if (await FindRunningPortAsync() is { } running)
    {
        return running;
    }

    if (ResolveServerCommand() is not { } server)
    {
        Console.Error.WriteLine(
            $"No daemon is running, and the Aiko daemon was not found next to the aiko CLI ({AppContext.BaseDirectory}). Run the installer first.");
        return null;
    }

    Console.WriteLine("No daemon is running; starting one in the background.");
    if (await StartDetachedAsync(server) is not { } started)
    {
        return null;
    }

    PrintStartedInBackground(started);
    return started.Port;
}

// Where a background daemon writes its log: AIKO_LOG_FILE when set, beside the database otherwise. The server
// reads the same variable, so the path the CLI prints is the path the daemon uses.
static string ResolveLogPath()
{
    if (Environment.GetEnvironmentVariable("AIKO_LOG_FILE") is { Length: > 0 } configured)
    {
        return configured;
    }

    var directory = Path.GetDirectoryName(AikoDataPaths.FromEnvironment().DatabasePath)
        ?? throw new InvalidOperationException("The database path must include a directory.");
    return Path.Combine(directory, "daemon.log");
}

// The tail of a daemon log, printed when one fails to start: the reason is almost always in the last lines of
// the daemon's own account.
static void PrintLogTail(string logPath)
{
    try
    {
        if (!File.Exists(logPath))
        {
            Console.Error.WriteLine($"No log was written to {logPath}.");
            return;
        }

        Console.Error.WriteLine($"Last lines of {logPath}:");
        foreach (var line in File.ReadLines(logPath).TakeLast(15))
        {
            Console.Error.WriteLine($"  {line}");
        }
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"The log at {logPath} could not be read: {exception.Message}");
    }
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
    // The UI needs a daemon to talk to. Starting one is what the person meant by asking to see the board,
    // and being told to start a service first is not an answer to that.
    if (await EnsureDaemonAsync() is not { } port)
    {
        Console.Error.WriteLine("The UI was not opened because no daemon is answering. Run `aiko serve` to see why.");
        return 1;
    }

    var token = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pair-request");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    var code = document.RootElement.GetProperty("code").GetString()
        ?? throw new InvalidOperationException("The daemon returned no pairing code.");

    var url = $"http://127.0.0.1:{port}/#pair={Uri.EscapeDataString(code)}";
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    Console.WriteLine($"Opened {url}");
    return 0;
}

// `aiko autostart enable|disable|status` - the daemon started at sign-in. The installer offers the choice
// and `aiko uninstall` takes it back, and both go through this one command: the entry has one writer and one
// remover, so the file the installer leaves behind cannot be a different file from the one uninstall looks
// for.
static int Autostart(string[] args)
{
    var installation = new AikoInstallation(AikoDataPaths.FromEnvironment());
    // A per-user startup folder only exists on Windows, so the entry names the Windows command the installer
    // publishes; on any other platform IsSupported is false and the path is never used.
    var autostart = new AikoAutostart(Path.Combine(installation.BinDirectory, "aiko.exe"));
    var action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";

    if (!autostart.IsSupported)
    {
        Console.Error.WriteLine(
            "This machine has no per-user startup folder, so the daemon cannot be started at sign-in. " +
            "Start it with `aiko serve --detached` instead.");
        return 1;
    }

    switch (action)
    {
        case "enable":
            if (!File.Exists(autostart.ExecutablePath))
            {
                // An entry pointing at a missing command starts nothing at every sign-in and says nothing
                // about why, which is worse than refusing now.
                Console.Error.WriteLine(
                    $"The aiko command was not found at {autostart.ExecutablePath}. Run the installer first.");
                return 1;
            }

            autostart.Enable();
            Console.WriteLine($"The daemon will start at sign-in: {autostart.EntryPath}");
            return 0;
        case "disable":
            Console.WriteLine(autostart.Disable()
                ? $"Removed {autostart.EntryPath}; the daemon no longer starts at sign-in."
                : "The daemon was not set to start at sign-in; nothing to remove.");
            return 0;
        case "status":
            Console.WriteLine(autostart.IsEnabled
                ? $"The daemon starts at sign-in: {autostart.EntryPath}"
                : "The daemon does not start at sign-in.");
            return 0;
        default:
            Console.Error.WriteLine(
                $"Unknown argument for `aiko autostart`: {action}. Use enable, disable or status.");
            return 2;
    }
}

// `aiko uninstall` takes back what the installer put on the machine. The three things are separate on
// purpose - the PATH entry, the published binaries, the data - and the data is never removed unless it is
// asked for by name, because it holds every registered project, the event journal and the access token.
static async Task<int> UninstallAsync(string[] args)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var installation = new AikoInstallation(dataPaths);
    Console.WriteLine($"Install directory: {installation.BinDirectory}");
    Console.WriteLine($"Data directory:    {installation.DataDirectory}");

    // The project list is read before anything is deleted: it is what the .aiko question is about, and it
    // lives in the database the last step may remove.
    var projects = await ReadRegisteredProjectsAsync(dataPaths);

    var stopped = await TryStopDaemonAsync(ReadOption(args, "--port"));
    Console.WriteLine(stopped ? "Stopped the running daemon." : "No daemon was running.");

    // Before the binaries go: an entry that outlives the command it starts would try to launch something
    // that is no longer there on every sign-in, and say nothing about why.
    var autostart = new AikoAutostart(Path.Combine(installation.BinDirectory, "aiko.exe"));
    var autostartRemoved = autostart.Disable();
    Console.WriteLine(autostartRemoved
        ? "Removed the start-at-sign-in entry."
        : "The daemon was not set to start at sign-in.");

    var pathRemoved = installation.RemoveFromUserPath();
    Console.WriteLine(pathRemoved
        ? $"Removed {installation.BinDirectory} from the user PATH; open a new terminal."
        : "The user PATH does not name the install directory.");

    var binaries = installation.RemoveBinaries();
    if (!binaries.Removed)
    {
        Console.WriteLine("The published binaries were not there.");
    }
    else if (binaries.Blocked.Count == 0)
    {
        Console.WriteLine("Removed the published binaries.");
    }
    else
    {
        // The running `aiko` is one of these files, so an uninstall started from it keeps something back -
        // which is worth saying rather than hiding behind a "done".
        Console.WriteLine($"Kept {binaries.Blocked.Count} file(s) that are in use, including this one:");
        foreach (var file in binaries.Blocked)
        {
            Console.WriteLine($"  {file}");
        }

        Console.WriteLine("Delete them after this command has exited.");
    }

    // Neither kind of data goes by default, and each is asked about on its own: a project's .aiko is its
    // own work, and the data directory is this installation's registration of every project.
    var projectDataRemoved = RemoveProjectData(installation, projects, args);
    var dataRemoved = RemoveData(installation, args);

    Console.WriteLine();
    if (!pathRemoved && !binaries.Removed && !projectDataRemoved && !dataRemoved && !stopped && !autostartRemoved)
    {
        Console.WriteLine("Nothing was left to remove: Aiko is not installed here any more.");
    }

    return 0;
}

// The registered projects, or none when the daemon has never run here. A missing database is not an error on
// the way out: it is the second run of an uninstall.
static async Task<IReadOnlyList<RegisteredProject>> ReadRegisteredProjectsAsync(AikoDataPaths dataPaths)
{
    if (!File.Exists(dataPaths.DatabasePath))
    {
        return [];
    }

    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    return await new SqliteProjectCatalog(database).ListAsync(CancellationToken.None);
}

// The daemon's own data goes only when it is named: `--remove-data`, or a yes at the prompt. `--yes` on its
// own is consent to the uninstall, not to deleting a machine's history, so it does not count here.
static bool RemoveData(AikoInstallation installation, string[] args)
{
    if (!Directory.Exists(installation.DataDirectory))
    {
        Console.WriteLine("No data directory to remove.");
        return false;
    }

    var confirmed = HasFlag(args, "--remove-data") ||
        (!HasFlag(args, "--yes", "-y") &&
         Confirm(
             $"Remove {installation.DataDirectory} - database, settings, access token and backups? " +
             "Projects keep their own .aiko"));
    if (!confirmed)
    {
        Console.WriteLine($"Kept {installation.DataDirectory}. Remove it with `aiko uninstall --remove-data`.");
        return false;
    }

    ReportRemoval(installation.RemoveData(), installation.DataDirectory);
    return true;
}

// Project data is a separate question with a separate flag. These directories are the users' own work rather
// than this installation's files, so they are listed before the question is asked.
static bool RemoveProjectData(
    AikoInstallation installation,
    IReadOnlyList<RegisteredProject> projects,
    string[] args)
{
    if (projects.Count == 0)
    {
        return false;
    }

    var roots = projects
        .Select(project => project.RootPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    Console.WriteLine($"{roots.Length} registered project(s) have their own .aiko directory:");
    foreach (var root in roots)
    {
        Console.WriteLine($"  {AikoProjectPaths.DataRoot(root)}");
    }

    var confirmed = HasFlag(args, "--remove-project-data") ||
        (!HasFlag(args, "--yes", "-y") &&
         Confirm("Remove those .aiko directories? Their cards, memory and artifacts are deleted with them"));
    if (!confirmed)
    {
        Console.WriteLine("Kept every project's .aiko directory.");
        return false;
    }

    foreach (var root in roots)
    {
        ReportRemoval(installation.RemoveProjectData(root), AikoProjectPaths.DataRoot(root));
    }

    return true;
}

// Reports one removal the way an uninstall has to: what went, and what stayed because it is still in use.
static void ReportRemoval(InstallationRemoval removal, string path)
{
    if (removal.Blocked.Count == 0)
    {
        Console.WriteLine($"Removed {path}.");
        return;
    }

    Console.WriteLine($"Removed {path}, except {removal.Blocked.Count} entry(ies) still in use:");
    foreach (var blocked in removal.Blocked)
    {
        Console.WriteLine($"  {blocked}");
    }
}

// Stopping the daemon is a courtesy, not a condition: a second uninstall finds nothing running, and that is
// the ordinary case rather than a failure. The stop goes through the daemon's own endpoint so it can finish
// what it is doing, exactly as `aiko serve stop` does.
static async Task<bool> TryStopDaemonAsync(string? requestedPort)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var port = requestedPort;
    if (string.IsNullOrWhiteSpace(port))
    {
        // No saved port means no daemon ever ran here, so there is nothing to stop - and reading the token
        // would create the data directory this command may be about to remove.
        port = (await new DaemonEndpointConfiguration(dataPaths).TryReadAsync())?.Port.ToString();
    }

    if (string.IsNullOrWhiteSpace(port))
    {
        return false;
    }

    var accessToken = Environment.GetEnvironmentVariable("AIKO_TOKEN");
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        accessToken = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    }

    using var http = new HttpClient
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(5)
    };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    try
    {
        using var response = await http.PostAsync("/api/v1/system/shutdown", content: null);
        return response.IsSuccessStatusCode;
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        return false;
    }
}

// `aiko settings get|set` - the project's own settings, or a template's defaults. There is no
// installation-level document: a project is created from a template and owns its copy from then on, so a
// command that offered "global settings" would be the first half of the misunderstanding §24 of the
// specification had to clear up.
static async Task<int> SettingsAsync(string[] args)
{
    var action = args.Length > 1 ? args[1].ToLowerInvariant() : "get";
    var projectId = ReadOption(args, "--project");
    var templateId = ReadOption(args, "--template");
    if (string.IsNullOrWhiteSpace(projectId) == string.IsNullOrWhiteSpace(templateId))
    {
        Console.Error.WriteLine(
            "Name exactly one target: --project <id> for a project's own settings, or --template <id> for " +
            "the defaults new projects start from. There is no installation-level settings document.");
        return 2;
    }

    if (action is not ("get" or "set"))
    {
        Console.Error.WriteLine($"Unknown argument for `aiko settings`: {action}. Use get or set.");
        return 2;
    }

    var settingsPath = string.IsNullOrWhiteSpace(projectId)
        ? $"api/v1/templates/{templateId}/settings"
        : $"api/v1/projects/{projectId}/settings";
    // A template is read whole - its settings are one section of the document that also carries its
    // pipelines - and written through the settings slice, which is the same body a project takes.
    var readPath = string.IsNullOrWhiteSpace(projectId) ? $"api/v1/templates/{templateId}" : settingsPath;

    using var http = await CreateDaemonClientAsync(ReadOption(args, "--port"));
    if (http is null)
    {
        return 1;
    }

    if (action is "get")
    {
        return await SendAsync(http, HttpMethod.Get, readPath, body: null);
    }

    var document = await ReadDocumentAsync(args);
    return document is null
        ? 2
        : await SendAsync(http, HttpMethod.Put, settingsPath, document);
}

// `aiko workflow get|set` - the project's pipelines. Reading takes the project's own file, because it changes
// nothing and there is no endpoint for it; writing goes through the daemon, which validates the definition,
// refuses to strand a card in a stage that was removed, and reprojects the board afterwards.
static async Task<int> WorkflowAsync(string[] args)
{
    var action = args.Length > 1 ? args[1].ToLowerInvariant() : "get";
    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("Pass the project: --project <id>.");
        return 2;
    }

    if (action is "get")
    {
        return await ReadWorkflowsAsync(projectId, ReadOption(args, "--workflow"));
    }

    if (action is not "set")
    {
        Console.Error.WriteLine($"Unknown argument for `aiko workflow`: {action}. Use get or set.");
        return 2;
    }

    var document = await ReadDocumentAsync(args);
    if (document is null)
    {
        return 2;
    }

    WorkflowDefinition workflow;
    try
    {
        workflow = JsonSerializer.Deserialize(document, CliJsonContext.Default.WorkflowDefinition)
            ?? throw new JsonException("the document is empty");
    }
    catch (JsonException exception)
    {
        Console.Error.WriteLine($"The document is not a workflow definition: {exception.Message}");
        return 2;
    }

    // The stored document carries the revision it was read at; the update body names the same number
    // `expectedRevision`, which is the one field the two shapes spell differently.
    var update = new WorkflowUpdate(
        workflow.Title,
        workflow.Stages,
        workflow.Revision,
        workflow.Description,
        workflow.Icon,
        workflow.Color,
        workflow.BlendsWithParent);

    using var http = await CreateDaemonClientAsync(ReadOption(args, "--port"));
    if (http is null)
    {
        return 1;
    }

    return await SendAsync(
        http,
        HttpMethod.Put,
        $"api/v1/projects/{projectId}/workflows/{workflow.Id}",
        JsonSerializer.Serialize(update, CliJsonContext.Default.WorkflowUpdate));
}

// The project's pipelines as its own file states them. Read-only, and therefore offline: nothing here
// changes, so nothing here needs the daemon - the same reason `aiko status` reads its facts directly.
static async Task<int> ReadWorkflowsAsync(string projectId, string? workflowId)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var definitions = new FileProjectDefinitionStore(new SqliteProjectCatalog(database));

    ProjectBoardDefinition definition;
    try
    {
        definition = await definitions.ReadAsync(projectId, CancellationToken.None);
    }
    catch (KeyNotFoundException)
    {
        Console.Error.WriteLine($"No registered project with id {projectId}. `aiko doctor` lists them.");
        return 1;
    }

    if (workflowId is null)
    {
        foreach (var workflow in definition.Workflows)
        {
            Console.WriteLine(
                $"{workflow.Id}  v{workflow.Revision}  {workflow.Title}  ({workflow.Stages.Count} stages)");
        }

        return 0;
    }

    var found = definition.Workflows.FirstOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.Id, workflowId));
    if (found is null)
    {
        Console.Error.WriteLine($"Project {projectId} has no workflow {workflowId}.");
        return 1;
    }

    // The document is what `set` reads back, so the round trip is get, edit, set - and nothing in between
    // has to be retyped.
    Console.WriteLine(JsonSerializer.Serialize(found, CliJsonContext.Default.WorkflowDefinition));
    return 0;
}

// The daemon's address and credential, for the commands that go through it. Writes belong to the daemon
// because it owns the projections and the event stream: a file edited behind its back leaves the board
// showing yesterday's pipelines until someone reindexes.
static async Task<HttpClient?> CreateDaemonClientAsync(string? requestedPort)
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var port = requestedPort;
    if (string.IsNullOrWhiteSpace(port))
    {
        port = (await new DaemonEndpointConfiguration(dataPaths).TryReadAsync())?.Port.ToString();
    }

    if (string.IsNullOrWhiteSpace(port))
    {
        Console.Error.WriteLine("No daemon port is known yet. Start one with `aiko serve -d`.");
        return null;
    }

    var accessToken = Environment.GetEnvironmentVariable("AIKO_TOKEN");
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        accessToken = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    }

    var http = new HttpClient
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(15)
    };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    return http;
}

// One request, answered in the daemon's own words. The body goes out exactly as it was read, so what a person
// edits and what the daemon receives cannot differ by anything this command did.
static async Task<int> SendAsync(HttpClient http, HttpMethod method, string path, string? body)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
    {
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    try
    {
        using var response = await http.SendAsync(request);
        var answer = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            // Not summarised: the daemon names the stage that would have been emptied of a card, the
            // revision it expected, and everything else it refused the document for.
            Console.Error.WriteLine($"The daemon answered {(int)response.StatusCode}: {answer}");
            return 1;
        }

        Console.WriteLine(answer);
        return 0;
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine($"No daemon is answering on {http.BaseAddress}. Start one with `aiko serve -d`.");
        return 1;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine($"The daemon on {http.BaseAddress} did not answer in time.");
        return 1;
    }
}

// The document to write: inline for a small change, or a file for a whole one.
static async Task<string?> ReadDocumentAsync(string[] args)
{
    if (ReadOption(args, "--json") is { Length: > 0 } inline)
    {
        return inline;
    }

    if (ReadOption(args, "--file") is { Length: > 0 } file)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"No such document: {file}");
            return null;
        }

        return await File.ReadAllTextAsync(file);
    }

    Console.Error.WriteLine("Pass the document with --json \"<document>\" or --file <path>.");
    return null;
}

// `aiko backup` writes the archive `aiko_backup` has always written - a zip of the project's .aiko contents
// - and then says what the archive holds, because "done" is a claim and the entry count is its evidence.
static async Task<int> BackupAsync(string[] args)
{
    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("Pass the project: --project <id>.");
        return 2;
    }

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var project = await new SqliteProjectCatalog(database).FindAsync(projectId, CancellationToken.None);
    if (project is null)
    {
        Console.Error.WriteLine($"No registered project with id {projectId}. `aiko doctor` lists them.");
        return 1;
    }

    var source = AikoProjectPaths.DataRoot(project.RootPath);
    // Beside the database and named the way `aiko_backup` names it, unless the caller says otherwise.
    var target = ReadOption(args, "--output") ?? Path.Combine(
        Path.GetDirectoryName(dataPaths.DatabasePath)!,
        "backups",
        $"{project.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

    try
    {
        var summary = await ProjectArchive.CreateAsync(source, target, CancellationToken.None);
        Console.WriteLine(
            $"Archived {project.Id} to {summary.Path}: {summary.Entries} entries, {summary.Bytes} bytes.");
        return 0;
    }
    catch (DirectoryNotFoundException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

// `aiko restore` takes an archive back over a project. It overwrites files, so it asks first - and it
// reprojects afterwards, because the restored files and the board have just parted ways.
static async Task<int> RestoreAsync(string[] args)
{
    var archivePath = ReadOption(args, "--archive");
    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(projectId))
    {
        Console.Error.WriteLine("Pass the archive and the project: --archive <path> --project <id>.");
        return 2;
    }

    if (!File.Exists(archivePath))
    {
        Console.Error.WriteLine($"No such archive: {archivePath}");
        return 1;
    }

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var project = await catalog.FindAsync(projectId, CancellationToken.None);
    if (project is null)
    {
        Console.Error.WriteLine($"No registered project with id {projectId}. `aiko doctor` lists them.");
        return 1;
    }

    var target = AikoProjectPaths.DataRoot(project.RootPath);
    if (!HasFlag(args, "--yes", "-y") &&
        !Confirm($"Restore {archivePath} over {target}? Files with the same names are overwritten"))
    {
        Console.Error.WriteLine("Cancelled. Nothing changed.");
        return 1;
    }

    try
    {
        var written = await ProjectArchive.ExtractAsync(archivePath, target, CancellationToken.None);
        Console.WriteLine($"Restored {written} file(s) into {target}.");
    }
    catch (InvalidDataException exception)
    {
        // The archive would have written outside the project; nothing was unpacked.
        Console.Error.WriteLine(exception.Message);
        return 1;
    }

    // Until this runs the board would keep showing what was there before the restore: the files it reads
    // and the projections it draws are two different things.
    var reindexed = await new ProjectReindexer(catalog, database)
        .ReindexAsync(projectId, CancellationToken.None);
    Console.WriteLine(
        $"Reindexed {projectId}: {reindexed.Cards} cards, {reindexed.Relations} relations, " +
        $"{reindexed.MemoryDocuments} memory documents.");
    return 0;
}

// `aiko logs` - the tail of the daemon's own log, and with a project, the tail of its event journal. Both
// are read-only views of what is already stored: the file for the daemon, the daemon's own history endpoint
// for the journal, so the CLI never reaches into the database itself.
static async Task<int> LogsAsync(string[] args)
{
    var lines = ReadOption(args, "--lines") is { Length: > 0 } requested &&
                int.TryParse(requested, out var parsed) &&
                parsed > 0
        ? parsed
        : 50;

    var logPath = ResolveLogPath();
    Console.WriteLine($"Daemon log ({logPath}):");
    if (File.Exists(logPath))
    {
        foreach (var line in ReadTail(logPath, lines))
        {
            Console.WriteLine($"  {line}");
        }
    }
    else
    {
        // Two empty cases, and both are said rather than left to silence: this one is a daemon that has never
        // run in the background, the one below is a project that has never recorded anything.
        Console.WriteLine(
            "  no log file yet: it is written when the daemon runs in the background, `aiko serve -d`.");
    }

    var projectId = ReadOption(args, "--project");
    if (string.IsNullOrWhiteSpace(projectId))
    {
        Console.WriteLine();
        Console.WriteLine("Pass --project <id> to read that project's execution journal.");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"Execution journal of {projectId}, last {lines}:");
    return await ReadJournalAsync(projectId, lines, ReadOption(args, "--after"), ReadOption(args, "--port"));
}

// The last lines of a file, without holding the whole log in memory: a daemon log is written to run for weeks.
static IReadOnlyList<string> ReadTail(string path, int lines)
{
    var tail = new Queue<string>(lines);
    foreach (var line in File.ReadLines(path))
    {
        if (tail.Count == lines)
        {
            tail.Dequeue();
        }

        tail.Enqueue(line);
    }

    return tail.ToArray();
}

// The journal is read ascending, so the last entries are found by walking forward and keeping the tail. The
// walk is bounded, and it says so when the bound is reached rather than quietly showing a window that is not
// the last one.
static async Task<int> ReadJournalAsync(string projectId, int lines, string? after, string? port)
{
    const int Page = 500;
    const int MaxPages = 20;

    var cursor = long.TryParse(after, out var parsedAfter) ? parsedAfter : 0;
    var tail = new Queue<AikoEvent>(lines);
    var truncated = false;

    using var http = await CreateDaemonClientAsync(port);
    if (http is null)
    {
        return 1;
    }

    for (var page = 0; page < MaxPages; page++)
    {
        var body = await GetAsync(
            http,
            $"api/v1/projects/{projectId}/events/history?after={cursor}&limit={Page}");
        if (body is null)
        {
            return 1;
        }

        IReadOnlyList<AikoEvent>? events;
        try
        {
            events = JsonSerializer.Deserialize(body, CliJsonContext.Default.IReadOnlyListAikoEvent);
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"The daemon's answer is not a journal page: {exception.Message}");
            return 1;
        }

        if (events is null || events.Count == 0)
        {
            break;
        }

        foreach (var @event in events)
        {
            if (tail.Count == lines)
            {
                tail.Dequeue();
            }

            tail.Enqueue(@event);
        }

        cursor = events[^1].Id;
        if (events.Count < Page)
        {
            // A short page is the end of the journal, which is the ordinary way this loop finishes.
            break;
        }

        truncated = page == MaxPages - 1;
    }

    if (tail.Count == 0)
    {
        Console.WriteLine("  the project has no journal entries yet.");
        return 0;
    }

    foreach (var @event in tail)
    {
        Console.WriteLine(
            $"  [{@event.Id}] {@event.OccurredAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} " +
            $"{@event.Type} {OneLine(@event.PayloadJson)}");
    }

    if (truncated)
    {
        Console.WriteLine(
            $"  (the walk stopped after {MaxPages * Page} events; pass --after <id> to start further on)");
    }

    return 0;
}

// One event on one line. The payload is a JSON document of its own, and its line breaks would turn a journal
// tail into a screenful per entry - read as a log, not as a dump.
static string OneLine(string payload, int maxLength = 200)
{
    var collapsed = string.Join(' ', payload.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    return collapsed.Length <= maxLength ? collapsed : $"{collapsed[..maxLength]}…";
}

// A read from the daemon, answered in its own words - the same shape SendAsync gives a write.
static async Task<string?> GetAsync(HttpClient http, string path)
{
    try
    {
        using var response = await http.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"The daemon answered {(int)response.StatusCode}: {body}");
            return null;
        }

        return body;
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine($"No daemon is answering on {http.BaseAddress}. Start one with `aiko serve -d`.");
        return null;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine($"The daemon on {http.BaseAddress} did not answer in time.");
        return null;
    }
}

static async Task<int> StatusAsync()
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    Console.WriteLine($"Data directory: {Path.GetDirectoryName(dataPaths.DatabasePath)}");
    Console.WriteLine($"Database:        {dataPaths.DatabasePath}");

    await WriteAgentHintsAsync();

    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    if (settings is null)
    {
        Console.WriteLine("Daemon:          not started yet (no saved port)");
        WriteStartHint();
        WriteDaemonLogTail();
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
        WriteStartHint();
        WriteDaemonLogTail();
    }
    catch (OperationCanceledException)
    {
        // A daemon that does not answer within the timeout is stopped as far as the user is concerned;
        // an escaping TaskCanceledException here prints a stack trace instead of saying so.
        Console.WriteLine("Daemon:          not running (no answer)");
        WriteStartHint();
        WriteDaemonLogTail();
    }

    // The one failure that is invisible from the outside: an agent's own configuration still records the
    // old port, so the agent silently stops reaching Aiko while everything else looks healthy. The list
    // comes from the very inspection `aiko doctor` runs - a second check here is how the two would come to
    // disagree about the same files.
    var drift = (await InspectAsync(projectId: null)).Findings
        .Where(finding =>
            StringComparer.Ordinal.Equals(finding.Area, DiagnosticFinding.AgentConfigArea) &&
            finding.Severity != DiagnosticSeverity.Ok)
        .ToArray();
    if (drift.Length > 0)
    {
        Console.WriteLine($"Agent configs:   {drift.Length} stale:");
        foreach (var finding in drift)
        {
            Console.WriteLine($"  {finding.Summary}");
        }

        Console.WriteLine("                 run `aiko repair --fix` to rewrite them.");
    }

    return 0;
}

// An agent installed after Aiko is the ordinary case, and the fix is one command. `status` is what a person
// runs when something does not work, so it names the agents it found on PATH without Aiko's global
// configuration instead of leaving that discovery to the interface.
static async Task WriteAgentHintsAsync()
{
    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog, new FileProjectDefinitionStore(catalog));
    var discovered = await installer.DiscoverAsync(CancellationToken.None);
    var hints = discovered
        .Select(AgentConnectionHint.Describe)
        .Where(hint => hint is not null)
        .ToArray();
    if (hints.Length == 0)
    {
        return;
    }

    Console.WriteLine("Agents:          found on PATH without Aiko's global configuration:");
    foreach (var hint in hints)
    {
        Console.WriteLine($"  {hint}");
    }
}

// How a daemon is started, in the two ways it can be run.
static void WriteStartHint()
{
    Console.WriteLine("Start it with `aiko serve -d` (background) or `aiko serve` (this terminal); `aiko ui` starts one too.");
}

// The tail of the daemon's log, printed when it is not running: what a daemon said on its way out is the
// answer to "why did it stop?", and a log file only exists for one that was started in the background. The
// write time is shown with it so an older run's file is not read as this one's.
static void WriteDaemonLogTail()
{
    var logPath = ResolveLogPath();
    var file = new FileInfo(logPath);
    if (!file.Exists)
    {
        return;
    }

    Console.WriteLine($"Last log lines ({logPath}, written {file.LastWriteTime:yyyy-MM-dd HH:mm}):");
    foreach (var line in File.ReadLines(logPath).TakeLast(8))
    {
        Console.WriteLine($"  {line}");
    }
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
    var scope = ReadOption(args, "--scope");

    if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase))
    {
        // Without --agent the choice is what is really installed on this machine. The fixed list this branch
        // used to fall back on writes a global configuration into an agent that is not here, and on a machine
        // where none of the five is installed it wrote every one of them.
        var adapters = CreateAdapters();
        var targets = await AgentSelection.ResolveAsync(adapters, ReadOption(args, "--agent"), CancellationToken.None);
        if (targets.Count == 0)
        {
            Console.WriteLine(
                $"No agent installation was found on PATH, so there is nothing to {(uninstall ? "disconnect" : "connect")}. " +
                "Name one with --agent <ids> to act on it anyway.");
        }

        foreach (var adapter in adapters.Where(adapter => targets.Contains(adapter.Id)))
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

    // The project branch keeps the fixed fallback TASK-103 deliberately left alone: only the user scope asks
    // what is installed. `--agent <ids>` is honoured here exactly as it always was.
    var selected = ParseSelected(args);

    var dataPaths = AikoDataPaths.FromEnvironment();
    var database = new AikoDatabase(dataPaths);
    await database.InitializeAsync();
    var catalog = new SqliteProjectCatalog(database);
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog, new FileProjectDefinitionStore(catalog));

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

    // The endpoint is built from the registered project, not from the identifier the user typed: an id and a
    // handle both resolve here, and only the resolved project says which of the two belongs in the URL.
    if (await catalog.FindAsync(projectId, CancellationToken.None) is not { } project)
    {
        Console.Error.WriteLine($"No registered project with id {projectId}.");
        return 1;
    }

    var endpoint = ProjectMcpEndpoint.For($"http://127.0.0.1:{settings.Port}", project);
    // Without the token in the configuration the daemon answers 401 on /mcp and the agent never sees Aiko.
    var accessToken = await new AccessTokenStore(dataPaths).GetOrCreateAsync();
    var applied = await installer.ApplyAsync(
        project.Id,
        endpoint,
        accessToken,
        selected,
        CancellationToken.None);
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

// The fixed list the project branch still falls back on. The user branch does not use it: without --agent it
// asks which agents are installed (AgentSelection.ResolveAsync). The parsing of the option itself is shared,
// so there is one place that knows `--agent` is a comma-separated list.
static string[] ParseSelected(string[] args)
{
    var selected = AgentSelection.Parse(ReadOption(args, "--agent"));
    return selected.Count == 0 ? ["claude-code", "codex", "cursor", "zcode", "cline"] : [.. selected];
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
    new ZCodeAgentAdapter(),
    new ClineAgentAdapter()
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
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog, new FileProjectDefinitionStore(catalog));
    var settings = await new DaemonEndpointConfiguration(dataPaths).TryReadAsync();
    // Rewriting agent configurations is the point of a repair, so the token has to be at hand: the
    // configurations carry it, and a repair that dropped it would leave agents at 401.
    var accessToken = await new AccessTokenStore(dataPaths).GetOrCreateAsync();

    // A project created before slugs existed gets its readable handle here too, so a repair restores readable
    // URLs without waiting for the daemon to start. Idempotent: a project that already has one keeps it.
    var initializer = new ProjectInitializer(
        catalog,
        reindexer,
        new FileAppSettingsStore(catalog),
        new FileProjectTemplateStore(dataPaths));
    var slugsAssigned = await initializer.EnsureSlugsAsync(CancellationToken.None);
    if (slugsAssigned > 0)
    {
        Console.WriteLine($"Assigned a readable project id to {slugsAssigned} project(s).");
    }

    var registered = await catalog.ListAsync(CancellationToken.None);
    if (projectId is { Length: > 0 } requested)
    {
        registered = registered
            .Where(project =>
                StringComparer.Ordinal.Equals(project.Id, requested) ||
                StringComparer.Ordinal.Equals(project.Slug, requested))
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

        // Cards filed the old way are moved under .aiko/workflows here too: a repair is where a project is
        // brought back to the shape this build expects, and the step is idempotent - a second repair finds
        // nothing left to move.
        var migration = CardLayoutMigrator.Migrate(project.RootPath);
        if (migration.Moved.Count > 0)
        {
            Console.WriteLine(
                $"{project.Name}: filed {migration.Moved.Count} card collection(s) under .aiko/workflows " +
                $"({string.Join(", ", migration.Moved)}).");
        }

        var reindexed = await reindexer.ReindexAsync(project.Id, CancellationToken.None);
        Console.WriteLine(
            $"Reindexed {project.Name}: {reindexed.Cards} cards, {reindexed.Relations} relations, " +
            $"{reindexed.MemoryDocuments} memory documents.");

        if (settings is null || installedAdapters.Length == 0)
        {
            continue;
        }

        var endpoint = ProjectMcpEndpoint.For($"http://127.0.0.1:{settings.Port}", project);
        var applied = await installer.ApplyAsync(
            project.Id,
            endpoint,
            accessToken,
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
    var installer = new UnifiedAgentInstaller(CreateAdapters(), catalog, new FileProjectDefinitionStore(catalog));
    var cards = new FileCardStore(catalog, database);
    var diagnostics = new WorkshopDoctor(
        dataPaths,
        catalog,
        installer,
        CreateAdapters(),
        new DaemonEndpointConfiguration(dataPaths),
        new AccessTokenStore(dataPaths),
        cards,
        new FileProjectDefinitionStore(catalog),
        new SqliteExecutionCoordinator(
            catalog,
            cards,
            database,
            new AppSettingsService(new FileAppSettingsStore(catalog))));
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
