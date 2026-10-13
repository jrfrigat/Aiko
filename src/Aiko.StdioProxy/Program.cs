using System.Net;
using System.Net.Http.Headers;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var endpointValue = ReadEndpoint(args);
if (!TryValidateEndpoint(endpointValue, out var endpoint, out var error))
{
    await Console.Error.WriteLineAsync(error);
    return 2;
}

// The daemon authenticates every REST and MCP call, and this proxy is a client of it like the CLI is: the
// token the daemon was started with when AIKO_TOKEN names one, and the stored token - the file beside the
// daemon's database - otherwise. Without this the proxy answered 401 to everything, and an agent configured
// to speak to Aiko over stdio simply did not work (TASK-161).
var dataPaths = AikoDataPaths.FromEnvironment();
var accessToken = Environment.GetEnvironmentVariable("AIKO_TOKEN");
if (string.IsNullOrWhiteSpace(accessToken))
{
    try
    {
        accessToken = (await new AccessTokenStore(dataPaths).GetOrCreateAsync()).Trim();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        await Console.Error.WriteLineAsync(
            $"The access token could not be read from {dataPaths.AccessTokenPath}: {exception.Message} " +
            "Start the daemon once, or run this proxy with AIKO_TOKEN set to the token it was started with.");
        return 2;
    }
}

if (string.IsNullOrWhiteSpace(accessToken))
{
    await Console.Error.WriteLineAsync(
        $"No access token: {dataPaths.AccessTokenPath} is empty and AIKO_TOKEN is not set. " +
        "Start the daemon once so that it writes one, or run this proxy with AIKO_TOKEN set to the token it " +
        "was started with.");
    return 2;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    using var handler = new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false
    };
    using var httpClient = new HttpClient(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    await using var httpTransport = new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Name = "Aiko Streamable HTTP",
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(10),
            EnableStandaloneGetStream = false
        },
        httpClient,
        loggerFactory: null,
        ownsHttpClient: false);
    await using var remote = await httpTransport.ConnectAsync(shutdown.Token);
    await using var stdio = new StdioServerTransport("Aiko stdio proxy");

    var toRemote = ForwardAsync(stdio, remote, shutdown.Token);
    var toStdio = ForwardAsync(remote, stdio, shutdown.Token);
    await Task.WhenAny(toRemote, toStdio);
    await shutdown.CancelAsync();

    try
    {
        await Task.WhenAll(toRemote, toStdio);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
    }

    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    return 0;
}
catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
{
    // The token this proxy holds is not the one the daemon checks. Saying so is the whole point: a 401 from
    // the transport reads as "the daemon is broken" to whoever configured the agent (TASK-161).
    await Console.Error.WriteLineAsync(
        "The daemon refused the access token (401). If it was started with AIKO_TOKEN, run this proxy with the " +
        $"same value in the environment; otherwise check the stored token at {dataPaths.AccessTokenPath}.");
    return 1;
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Aiko stdio proxy failed: {exception.Message}");
    return 1;
}

static async Task ForwardAsync(
    ITransport source,
    ITransport destination,
    CancellationToken cancellationToken)
{
    await foreach (var message in source.MessageReader.ReadAllAsync(cancellationToken))
    {
        await destination.SendMessageAsync(message, cancellationToken);
    }
}

static string? ReadEndpoint(string[] arguments)
{
    if (arguments.Length == 2 &&
        string.Equals(arguments[0], "--url", StringComparison.Ordinal))
    {
        return arguments[1];
    }

    if (arguments.Length == 0)
    {
        return Environment.GetEnvironmentVariable("AIKO_MCP_URL");
    }

    return null;
}

static bool TryValidateEndpoint(
    string? value,
    out Uri endpoint,
    out string error)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        endpoint = null!;
        error =
            "Usage: aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/{projectHandle}";
        return false;
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out endpoint!) ||
        !endpoint.IsLoopback ||
        (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
        !string.IsNullOrEmpty(endpoint.UserInfo) ||
        !string.IsNullOrEmpty(endpoint.Fragment) ||
        !endpoint.AbsolutePath.StartsWith("/mcp/projects/", StringComparison.Ordinal))
    {
        error = "The Aiko MCP URL must be an absolute loopback HTTP project endpoint.";
        return false;
    }

    error = string.Empty;
    return true;
}
