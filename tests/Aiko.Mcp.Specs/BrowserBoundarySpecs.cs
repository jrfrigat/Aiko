using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The browser boundary of the daemon: only the daemon's own page may talk to it. Any loopback origin used to
/// pass, on any port, and the session cookie - which the browser sends to every port of localhost - opened the
/// MCP endpoint too, so a page served by some other local program could call Aiko's tools blind.
/// </summary>
public class BrowserBoundarySpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task Only_the_daemons_own_origin_passes()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        var own = fixture.BaseUrl.GetLeftPart(UriPartial.Authority);

        Assert.Equal(HttpStatusCode.OK, await StatusWithOriginAsync(http, own));
        Assert.Equal(HttpStatusCode.OK, await StatusWithOriginAsync(http, $"http://localhost:{fixture.BaseUrl.Port}"));

        // Another program on this machine is still another origin.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusWithOriginAsync(http, $"http://127.0.0.1:{fixture.BaseUrl.Port + 1}"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusWithOriginAsync(http, "http://localhost:5173"));
    }

    [Fact]
    public async Task A_request_the_browser_marks_as_from_another_site_is_refused()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };

        Assert.Equal(HttpStatusCode.Forbidden, await StatusWithFetchSiteAsync(http, "cross-site"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusWithFetchSiteAsync(http, "same-site"));
        Assert.Equal(HttpStatusCode.OK, await StatusWithFetchSiteAsync(http, "same-origin"));
        Assert.Equal(HttpStatusCode.OK, await StatusWithFetchSiteAsync(http, "none"));
    }

    [Fact]
    public async Task No_page_may_frame_the_daemon()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.GetAsync("/health");

        Assert.Contains(
            "frame-ancestors 'none'",
            string.Join(";", response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_cookie_opens_the_board_but_not_mcp_nor_a_bare_post()
    {
        var serverDll = AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Boundary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var server = SpecDaemon.Start(
            serverDll,
            root,
            ("AIKO_DATABASE", Path.Combine(root, "data", "aiko.db")),
            ("AIKO_PORT", port.ToString(CultureInfo.InvariantCulture)),
            ("AIKO_TOKEN", "boundary-token-123"),
            ("AIKO_PAIR_CODE", "boundary-pair"));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitForHealthAsync(http);

            // Pairing is how the browser gets its session cookie; the handler keeps it from here on.
            using (var pair = new StringContent("{\"code\":\"boundary-pair\"}", Encoding.UTF8, "application/json"))
            using (var paired = await http.PostAsync("/api/v1/auth/pair", pair))
            {
                Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
            }

            using (var board = await http.GetAsync("/api/v1/projects"))
            {
                Assert.Equal(HttpStatusCode.OK, board.StatusCode);
            }

            // The MCP endpoint is for agents, which carry the token: the browser's cookie does not open it.
            using (var mcp = new StringContent("{}", Encoding.UTF8, "text/plain"))
            using (var mcpResponse = await http.PostAsync("/mcp/projects/any", mcp))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, mcpResponse.StatusCode);
            }

            // A write the cookie authenticates has to say it comes from the page: a bare POST is what a form on
            // another page can send without asking.
            using (var bare = await http.PostAsync("/api/v1/auth/pair-request", content: null))
            {
                Assert.Equal(HttpStatusCode.Forbidden, bare.StatusCode);
            }

            using (var marked = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pair-request"))
            {
                marked.Headers.Add("X-Aiko-Request", "1");
                using var response = await http.SendAsync(marked);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            // An agent with the token needs no such marker.
            using (var agent = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pair-request"))
            {
                agent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "boundary-token-123");
                using var agentClient = new HttpClient { BaseAddress = http.BaseAddress };
                using var response = await agentClient.SendAsync(agent);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            if (!server.Process.HasExited)
            {
                server.Process.Kill(entireProcessTree: true);
                await server.Process.WaitForExitAsync();
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A file the daemon still held; the temp directory is left for the system to clear.
            }
        }
    }

    private static async Task<HttpStatusCode> StatusWithOriginAsync(HttpClient http, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> StatusWithFetchSiteAsync(HttpClient http, string site)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Sec-Fetch-Site", site);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task WaitForHealthAsync(HttpClient http)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var health = await http.GetAsync("/health");
                if (health.IsSuccessStatusCode)
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

        throw new TimeoutException("The daemon did not answer /health.");
    }
}
