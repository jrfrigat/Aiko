using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A daemon whose stored token is empty must not become a daemon nobody has to authenticate against: the
/// token is compared for equality, and two empty strings are equal, so an empty Authorization header - or an
/// empty session cookie - opened the whole API. The store now replaces a token it would have refused to
/// write, the middleware refuses an empty presentation outright, and an environment token that short stops
/// the daemon instead of quietly protecting nothing (TASK-159).
/// </summary>
public sealed class ShortAccessTokenSpecs
{
    [Fact]
    public async Task An_empty_token_file_is_replaced_and_does_not_open_the_api()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.ShortToken.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "project"));
        var tokenPath = Path.Combine(root, "data", "access-token");
        await File.WriteAllTextAsync(tokenPath, string.Empty);

        var port = FindFreePort();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");
        using var server = SpecDaemon.Start(
            AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll"),
            root,
            ("AIKO_DATABASE", Path.Combine(root, "data", "aiko.db")),
            ("AIKO_PORT", port.ToString(CultureInfo.InvariantCulture)),
            ("AIKO_USER_HOME", Path.Combine(root, "home")));
        try
        {
            using var http = new HttpClient { BaseAddress = baseUrl };
            await WaitForHealthAsync(http);

            // The daemon kept running, and the token it now checks is one it wrote itself.
            var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
            Assert.True(token.Length >= 32, $"The daemon accepted an empty token file and stored '{token}'.");

            // An empty presentation is not the token, however empty the file once was. Sent without validation
            // on purpose: an empty Bearer value is exactly what a misconfigured client produces.
            using (var empty = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects"))
            {
                empty.Headers.TryAddWithoutValidation("Authorization", "Bearer ");
                using var response = await http.SendAsync(empty);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            using (var emptyCookie = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects"))
            {
                emptyCookie.Headers.TryAddWithoutValidation("Cookie", "aiko_session=");
                using var response = await http.SendAsync(emptyCookie);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            // And the token the daemon did write is the one that opens it: the fix is not "refuse everything".
            using (var authorized = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects"))
            {
                authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(authorized);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            server.Dispose();
            await Task.Delay(500);
            DeleteRootSafely(root);
        }
    }

    [Fact]
    public async Task A_token_under_the_floor_in_the_environment_stops_the_daemon()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.ShortToken.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));

        using var server = SpecDaemon.Start(
            AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll"),
            root,
            ("AIKO_DATABASE", Path.Combine(root, "data", "aiko.db")),
            ("AIKO_PORT", FindFreePort().ToString(CultureInfo.InvariantCulture)),
            ("AIKO_USER_HOME", Path.Combine(root, "home")),
            ("AIKO_TOKEN", "short"));
        try
        {
            // A daemon that protects nothing must not pretend to be running: it says why and stops.
            var exited = await Task.Run(() => server.Process.WaitForExit(30_000));
            Assert.True(exited, "The daemon kept running with an AIKO_TOKEN that protects nothing.");
            Assert.NotEqual(0, server.Process.ExitCode);
            var error = await server.Process.StandardError.ReadToEndAsync();
            Assert.Contains("AIKO_TOKEN", error, StringComparison.Ordinal);
        }
        finally
        {
            if (!server.Process.HasExited)
            {
                server.Dispose();
            }

            await Task.Delay(500);
            DeleteRootSafely(root);
        }
    }

    private static int FindFreePort()
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

    /// <summary>Removes the spec's own throwaway root, and only that one.</summary>
    private static void DeleteRootSafely(string root)
    {
        var specsRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Aiko.ShortToken.Specs"));
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
                Thread.Sleep(250);
            }
        }
    }
}
