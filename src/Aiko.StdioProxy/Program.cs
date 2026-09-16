using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var endpointValue = ReadEndpoint(args);
if (!TryValidateEndpoint(endpointValue, out var endpoint, out var error))
{
    await Console.Error.WriteLineAsync(error);
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
            "Usage: aiko-stdio --url http://127.0.0.1:<port>/mcp/projects/{projectId}";
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
