using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The stdio proxy is a client of the daemon, so it authenticates like every other client: with the
/// installation's access token. It did not, and every call it forwarded was answered 401 - which is
/// invisible in this suite, because the fixture's daemon runs with <c>AIKO_INSECURE=1</c> and a client
/// without a token passes there. This spec starts a daemon that enforces authentication, takes its token
/// from the file beside its database, and checks that the proxy works with nothing but the data directory
/// in its environment.
/// </summary>
public sealed class StdioProxyAuthSpecs
{
    [Fact]
    public async Task The_proxy_authenticates_with_the_token_the_daemon_wrote()
    {
        var serverDll = AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");
        var root = Path.Combine(Path.GetTempPath(), "Aiko.StdioAuth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "project"));
        var databasePath = Path.Combine(root, "data", "aiko.db");

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");

        // No AIKO_INSECURE and no AIKO_TOKEN: this daemon enforces authentication and uses the stored token,
        // which is the case the proxy has to work in.
        using var server = SpecDaemon.Start(
            serverDll,
            root,
            ("AIKO_DATABASE", databasePath),
            ("AIKO_PORT", port.ToString(CultureInfo.InvariantCulture)),
            ("AIKO_USER_HOME", Path.Combine(root, "home")));
        try
        {
            using var http = new HttpClient { BaseAddress = baseUrl };
            await WaitForHealthAsync(http);

            var tokenPath = Path.Combine(root, "data", "access-token");
            Assert.True(
                File.Exists(tokenPath),
                "A daemon that enforces authentication should have written its access token.");
            var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
            Assert.False(string.IsNullOrWhiteSpace(token));

            // The daemon really does refuse an anonymous client, so the check below cannot pass for a reason
            // of its own.
            using (var anonymous = new HttpClient { BaseAddress = baseUrl })
            {
                using var refused = await anonymous.GetAsync("/api/v1/projects");
                Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            }

            var projectId = await InitializeProjectAsync(http, token, root);
            var mcpEndpoint = new Uri($"{baseUrl}mcp/projects/{projectId}");
            var proxyAssembly = AikoServerFixture.FindRepositoryBinary("Aiko.StdioProxy", "aiko-stdio.dll");

            // An empty environment plus the data directory, and no AIKO_TOKEN: a machine with Aiko installed
            // carries one, and inheriting it would let the proxy pass for the wrong reason.
            var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            environment["AIKO_DATABASE"] = databasePath;
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Aiko stdio proxy",
                Command = "dotnet",
                Arguments = [proxyAssembly, "--url", mcpEndpoint.ToString()],
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment
            });

            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: CancellationToken.None);
            var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
            Assert.Contains(tools, tool => tool.Name == "aiko_get_project_context");
        }
        finally
        {
            server.Dispose();
            // Give the killed daemon a moment to release its SQLite files, then remove what is left: a root
            // that cannot be removed is not this spec's verdict.
            await Task.Delay(500);
            DeleteRootSafely(root);
        }
    }

    private static async Task WaitForHealthAsync(HttpClient http)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await http.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The daemon has not started listening yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The Aiko daemon did not become healthy at {http.BaseAddress}.");
    }

    private static async Task<string> InitializeProjectAsync(HttpClient http, string token, string root)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects/initialize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { rootPath = Path.Combine(root, "project") }),
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("The initialized project has no id.");
    }

    /// <summary>Removes the spec's own throwaway root, and only that one.</summary>
    private static void DeleteRootSafely(string root)
    {
        var specsRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Aiko.StdioAuth"));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var safePrefix = Path.TrimEndingDirectorySeparator(specsRoot) + Path.DirectorySeparatorChar;
        if (!normalizedRoot.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected test path: {normalizedRoot}");
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(normalizedRoot))
                {
                    Directory.Delete(normalizedRoot, true);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException && attempt < 4)
            {
                // A daemon that has just been killed may still hold a file. The last attempt lets the reason
                // out, because by then it is not a delay but a wrong path or a wrong permission.
                Thread.Sleep(250);
            }
        }
    }
}
