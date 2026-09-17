using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// Boots a disposable Aiko daemon on a random loopback port with an isolated
/// temporary database, initializes one project and exposes its MCP endpoint.
/// Requires the solution to be built (dotnet build/test on Aiko.slnx).
/// </summary>
public sealed class AikoServerFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Aiko.Mcp.Specs",
        Guid.NewGuid().ToString("N"));
    private Process? _server;

    /// <summary>
    /// Base URL of the running daemon, for example http://127.0.0.1:18123.
    /// </summary>
    public Uri BaseUrl { get; private set; } = null!;

    /// <summary>
    /// Streamable HTTP MCP endpoint of the initialized project.
    /// </summary>
    public Uri McpEndpoint { get; private set; } = null!;

    /// <summary>
    /// Identifier of the initialized test project.
    /// </summary>
    public string ProjectId { get; private set; } = null!;

    /// <summary>
    /// Root directory of the initialized test project, for specs that need to look at its own files.
    /// </summary>
    public string ProjectRoot => Path.Combine(_root, "project");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "project"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var serverDll = FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");

        var port = FindFreePort();
        BaseUrl = new Uri($"http://127.0.0.1:{port}");
        McpEndpoint = new Uri($"{BaseUrl}mcp/projects/{{0}}");

        _server = StartServer(serverDll, port);
        await WaitForHealthAsync();
        ProjectId = await InitializeProjectAsync();
        McpEndpoint = new Uri($"{BaseUrl}mcp/projects/{ProjectId}");
    }

    public async Task DisposeAsync()
    {
        if (_server is { } server)
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync(CancellationToken.None);
            server.Dispose();
        }

        // Give the killed server a moment to release its SQLite files.
        await Task.Delay(500);
        DeleteRootSafely();
    }

    /// <summary>
    /// Resolves the built stdio proxy executable (aiko-stdio.dll) from the repository.
    /// </summary>
    public string FindStdioProxyAssembly() =>
        FindRepositoryBinary("Aiko.StdioProxy", "aiko-stdio.dll");

    private Process StartServer(string serverDll, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverDll}\"",
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["AIKO_DATABASE"] =
            Path.Combine(_root, "data", "aiko.db");
        startInfo.EnvironmentVariables["AIKO_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        // The MCP spec suite exercises the protocol, not the local authentication layer.
        startInfo.EnvironmentVariables["AIKO_INSECURE"] = "1";
        // User-scope agent files (the /aiko-* skills and commands) land under the home directory, so the
        // daemon under test gets a throwaway one: a spec must not write into the machine running it.
        startInfo.EnvironmentVariables["AIKO_USER_HOME"] = Path.Combine(_root, "home");

        var server = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Aiko server process.");
        server.OutputDataReceived += (_, eventArgs) => Console.WriteLine($"[server] {eventArgs.Data}");
        server.ErrorDataReceived += (_, eventArgs) => Console.Error.WriteLine($"[server] {eventArgs.Data}");
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        return server;
    }

    private async Task WaitForHealthAsync()
    {
        using var httpClient = new HttpClient { BaseAddress = BaseUrl };
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (_server is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"The Aiko server exited early with code {_server.ExitCode}.");
            }

            try
            {
                using var response = await httpClient.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The server has not started listening yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The Aiko server did not become healthy at {BaseUrl}.");
    }

    private async Task<string> InitializeProjectAsync()
    {
        using var httpClient = new HttpClient { BaseAddress = BaseUrl };
        using var content = new StringContent(
            JsonSerializer.Serialize(new { rootPath = Path.Combine(_root, "project") }),
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await httpClient.PostAsync("/api/v1/projects/initialize", content);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The initialized project has no id.");
    }

    private int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>
    /// Locates the repository root by walking up from this assembly to the Aiko.slnx file.
    /// Uses Assembly.Location because the test host may run from a different directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AikoServerFixture).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException(
            "Could not locate the Aiko repository root from " +
            typeof(AikoServerFixture).Assembly.Location);
    }

    /// <summary>
    /// Resolves a built binary of another project, pinned to the configuration this test assembly was
    /// built with.
    /// </summary>
    /// <remarks>
    /// The previous "newest file under <c>bin/**</c>" rule looked convenient and was wrong twice over: it
    /// could pick a stale Release build over the Debug one just built, and when a rebuild failed
    /// silently - a daemon still holding the dll - the test then ran the old binary and reported a
    /// missing endpoint instead of a failed build.
    /// </remarks>
    public static string FindRepositoryBinary(string projectName, string fileName)
    {
        var binRoot = Path.Combine(FindRepositoryRoot(), "src", projectName, "bin", BuildConfiguration);
        string[] candidates =
        [
            Path.Combine(binRoot, "net10.0", fileName),
            Path.Combine(binRoot, "net10.0", "win-x64", fileName)
        ];

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"Build {projectName} ({BuildConfiguration}) before the specs: dotnet build Aiko.slnx",
                string.Join(" or ", candidates));
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private void DeleteRootSafely()
    {
        var specsRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Aiko.Mcp.Specs"));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var safePrefix = Path.TrimEndingDirectorySeparator(specsRoot) + Path.DirectorySeparatorChar;
        if (!normalizedRoot.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected test path: {normalizedRoot}");
        }

        if (Directory.Exists(normalizedRoot))
        {
            Directory.Delete(normalizedRoot, true);
        }
    }
}
