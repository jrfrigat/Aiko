using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The daemon answers the cockpit's index at every address that is not a real file or an endpoint of its
/// own - whatever the last segment looks like. The fallback used to be mapped as "index.html" alone, which
/// means "{*path:nonfile}": the nonfile route constraint refuses any last segment that looks like a file
/// name, so a release (always vX.Y.Z) answered 404 while its trailing-slash twin answered 200 (TASK-335).
/// </summary>
/// <remarks>
/// This spec starts a daemon of its own, with a web root it owns, because the built output carries no
/// wwwroot at all: on the shared fixture the fallback has no index to serve and would answer 404 even on
/// correct code - a spec that cannot tell the two apart is worth nothing here.
/// </remarks>
public sealed class SpaFallbackSpecs : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Aiko.SpaFallback.Specs",
        Guid.NewGuid().ToString("N"));
    private SpecDaemon? _server;
    private Uri _baseUrl = null!;

    public async Task InitializeAsync()
    {
        var webRoot = Path.Combine(_root, "webroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        await File.WriteAllTextAsync(
            Path.Combine(webRoot, "index.html"),
            "<!doctype html><title>Aiko</title>index marker");

        var serverDll = AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");
        var port = FindFreePort();
        _baseUrl = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");

        _server = SpecDaemon.Start(
            serverDll,
            _root,
            ("AIKO_DATABASE", Path.Combine(_root, "data", "aiko.db")),
            ("AIKO_PORT", port.ToString(CultureInfo.InvariantCulture)),
            ("AIKO_INSECURE", "1"),
            ("AIKO_USER_HOME", Path.Combine(_root, "home")),
            // The content root is the daemon's own directory and cannot be moved from a test; the web root
            // is the part that answers for the fallback, so that is the part this spec supplies.
            ("ASPNETCORE_WEBROOT", webRoot));

        // Nobody reads a redirected pipe, and a daemon that fills one stops serving.
        _server.Process.OutputDataReceived += (_, eventArgs) => Console.WriteLine($"[server] {eventArgs.Data}");
        _server.Process.ErrorDataReceived += (_, eventArgs) => Console.Error.WriteLine($"[server] {eventArgs.Data}");
        _server.Process.BeginOutputReadLine();
        _server.Process.BeginErrorReadLine();

        await WaitForHealthAsync();
    }

    public Task DisposeAsync()
    {
        _server?.Dispose();

        // One retry, for the moment the killed server still holds its SQLite files.
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            Thread.Sleep(500);
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_address_of_the_cockpit_that_looks_like_a_plain_page_is_served_the_index()
    {
        // The baseline, and the reason this spec may trust its own web root: an address with no dot in its
        // last segment is the case that always worked.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/p/flare/foobar"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/p/flare/board/cards/TASK-1"));
    }

    [Fact]
    public async Task An_address_whose_last_segment_looks_like_a_file_serves_the_index()
    {
        // A release is always named vX.Y.Z, so this is the shape a person reaches from the release screen -
        // and the shape the nonfile constraint used to refuse, with 404, while its trailing-slash twin
        // answered 200.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/p/flare/release/v0.44.0"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/p/flare/release/v0.44.0/"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/p/flare/foo.bar"));

        // And what comes back is the cockpit's index rather than some static file that happened to match.
        Assert.Contains("index marker", await BodyAsync("/p/flare/release/v0.44.0"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_api_address_is_still_a_404()
    {
        using var http = new HttpClient { BaseAddress = _baseUrl };

        // The one boundary the fallback must not cross: an address of the API that does not exist is a 404,
        // not the cockpit's page - and a dot in it does not change that either.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await http.GetAsync("/api/v1/no-such-endpoint")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await http.GetAsync("/api/v1/no-such-endpoint.json")).StatusCode);
    }

    private async Task<HttpStatusCode> StatusAsync(string address)
    {
        using var http = new HttpClient { BaseAddress = _baseUrl };
        using var response = await http.GetAsync(address);
        return response.StatusCode;
    }

    private async Task<string> BodyAsync(string address)
    {
        using var http = new HttpClient { BaseAddress = _baseUrl };
        using var response = await http.GetAsync(address);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task WaitForHealthAsync()
    {
        using var httpClient = new HttpClient { BaseAddress = _baseUrl };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_server?.Process.HasExited == true)
            {
                throw new InvalidOperationException(
                    $"The daemon exited with code {_server.Process.ExitCode} before it answered.");
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
                // Not listening yet.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("The daemon did not answer its health check.");
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
