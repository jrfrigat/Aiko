using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// What an agent is told when a tool call fails for a reason the daemon did not write, and what the daemon does
/// with the reason. The call-tool filter hands back the daemon's own words and deliberately leaves everything
/// else to the MCP layer, saying in a comment that the layer answers the agent in general terms and records the
/// cause in the log. That is a statement about a layer Aiko does not own, and until this spec it was neither
/// checked nor checkable - the daemon's console was redirected and never read (TASK-347).
/// </summary>
public sealed class ToolFailureSpecs
{
    [Fact]
    public async Task A_system_failure_answers_in_general_terms_and_the_daemon_records_the_reason()
    {
        using var daemon = await ToolFailureDaemon.StartAsync();
        var cardId = await daemon.CreateCardAsync();

        // The artifact is there and cannot be read: this spec holds it open with no sharing, so the daemon's
        // read fails in the I/O layer (an IOException naming the file), which is the class ErrorText calls the
        // machine's rather than Aiko's. That is exactly the failure the filter leaves to the MCP layer.
        var artifact = daemon.ArtifactPath(cardId, "analysis.md");
        using var held = new FileStream(artifact, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await using var client = await daemon.ConnectAsync();
        var result = await client.CallToolAsync(
            "aiko_get_card_artifact",
            new Dictionary<string, object?> { ["cardId"] = cardId, ["path"] = "analysis.md" },
            cancellationToken: CancellationToken.None);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        Assert.True(result.IsError, $"Reading an artifact that is a directory should fail, not answer '{text}'.");
        Assert.False(string.IsNullOrWhiteSpace(text), "The failure should say something.");

        // What the agent gets must not be the machine's layout: no path of ours, and not the exception's own
        // words either - the rule is about who wrote the text, not about how grave the failure is.
        Assert.DoesNotContain(artifact, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IOException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("being used by another process", text, StringComparison.OrdinalIgnoreCase);

        // And the reason is not lost: the daemon's own account of the run names the file it could not read,
        // which is exactly what the answer above was stripped of.
        var recorded = daemon.Output;
        Assert.True(
            recorded.Contains(artifact, StringComparison.OrdinalIgnoreCase),
            $"The daemon should have recorded the reason the tool call failed, with the file it could not read."
            + $"{Environment.NewLine}It said:{Environment.NewLine}{recorded}");
    }

    [Fact]
    public async Task A_refusal_the_daemon_wrote_still_reaches_the_agent_as_the_daemons_own_words()
    {
        using var daemon = await ToolFailureDaemon.StartAsync();
        await using var client = await daemon.ConnectAsync();

        // The other half of the same rule, on the same route: an unknown card is Aiko's own refusal, and the
        // agent is owed it - a filter that answered this in general terms would leave an agent guessing.
        var result = await client.CallToolAsync(
            "aiko_get_card_artifact",
            new Dictionary<string, object?> { ["cardId"] = "TASK-NOBODY-347", ["path"] = "analysis.md" },
            cancellationToken: CancellationToken.None);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        Assert.True(result.IsError, $"An unknown card should be refused, not answered with '{text}'.");
        Assert.False(string.IsNullOrWhiteSpace(text), "The refusal should say something.");
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A daemon of this spec's own with a project initialized in it, and an MCP client per connection. Its
    /// console is read while it runs, which is what lets a spec ask what the daemon recorded for itself
    /// (TASK-347).
    /// </summary>
    private sealed class ToolFailureDaemon : IDisposable
    {
        private readonly string _root;
        private readonly string _projectRoot;
        private readonly int _port;
        private readonly SpecDaemon _daemon;
        private readonly string _handle;
        private readonly StringBuilder _console = new();

        private ToolFailureDaemon(string root, string projectRoot, int port, SpecDaemon daemon, string handle)
        {
            _root = root;
            _projectRoot = projectRoot;
            _port = port;
            _daemon = daemon;
            _handle = handle;

            // The daemon's own account of the run is read here, the way the fixture reads its own daemon's:
            // the reason for a failure whose words are the runtime's is recorded there, and a spec asking
            // whether it was recorded has to see it (TASK-347).
            _daemon.Process.OutputDataReceived += (_, eventArgs) => Append(eventArgs.Data);
            _daemon.Process.ErrorDataReceived += (_, eventArgs) => Append(eventArgs.Data);
            _daemon.Process.BeginOutputReadLine();
            _daemon.Process.BeginErrorReadLine();
        }

        private void Append(string? line)
        {
            lock (_console)
            {
                _console.AppendLine(line);
            }
        }

        /// <summary>What the daemon has written to its console so far.</summary>
        public string Output
        {
            get
            {
                lock (_console)
                {
                    return _console.ToString();
                }
            }
        }

        public static async Task<ToolFailureDaemon> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "Aiko.ToolFailure", Guid.NewGuid().ToString("N"));
            var projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(root, "data"));
            var port = FreePort();
            var daemon = SpecDaemon.Start(
                AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll"),
                root,
                ("AIKO_DATABASE", Path.Combine(root, "data", "aiko.db")),
                ("AIKO_PORT", port.ToString(CultureInfo.InvariantCulture)),
                ("AIKO_INSECURE", "1"),
                ("AIKO_USER_HOME", Path.Combine(root, "home")));
            try
            {
                var handle = await InitializeProjectAsync(port, projectRoot);
                return new ToolFailureDaemon(root, projectRoot, port, daemon, handle);
            }
            catch
            {
                daemon.Dispose();
                throw;
            }
        }

        /// <summary>An MCP client for this project. Each connection is its own client, closed by the caller.</summary>
        public async Task<McpClient> ConnectAsync()
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(
                    $"http://127.0.0.1:{_port.ToString(CultureInfo.InvariantCulture)}/mcp/projects/{_handle}"),
                TransportMode = HttpTransportMode.StreamableHttp
            });
            return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
        }

        /// <summary>A card with one artifact saved, and its identifier.</summary>
        public async Task<string> CreateCardAsync()
        {
            await using var client = await ConnectAsync();
            var created = await client.CallToolAsync(
                "aiko_create_card",
                new Dictionary<string, object?>
                {
                    ["kind"] = "task",
                    ["title"] = $"An artifact that cannot be read ({Guid.NewGuid():N})",
                    ["ownPriority"] = 0
                },
                cancellationToken: CancellationToken.None);
            Assert.NotEqual(true, created.IsError);
            using var document = JsonDocument.Parse(Text(created));
            var cardId = document.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;

            var saved = await client.CallToolAsync(
                "aiko_save_card_artifact",
                new Dictionary<string, object?>
                {
                    ["cardId"] = cardId,
                    ["path"] = "analysis.md",
                    ["content"] = $"# {cardId}\n"
                },
                cancellationToken: CancellationToken.None);
            Assert.NotEqual(true, saved.IsError);
            return cardId;
        }

        /// <summary>
        /// Where an artifact of that card really is, found rather than assumed: the layout of a card's folder is
        /// the store's business, and a spec that hard-coded it would break over a change with nothing to do with
        /// this card.
        /// </summary>
        public string ArtifactPath(string cardId, string name) =>
            Directory
                .EnumerateFiles(_projectRoot, name, SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains(cardId, StringComparison.Ordinal))
            ?? throw new FileNotFoundException(
                $"The artifact '{name}' of {cardId} is not under {_projectRoot}.");

        public void Dispose()
        {
            _daemon.Dispose();
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(_root))
                    {
                        Directory.Delete(_root, recursive: true);
                    }

                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The daemon has just been ended and the system has not let go of its files yet.
                    Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
                }
            }
        }

        /// <summary>
        /// Registers the throwaway project through the API and answers with its handle: the MCP endpoint is
        /// addressed by the project it speaks for.
        /// </summary>
        private static async Task<string> InitializeProjectAsync(int port, string projectRoot)
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}")
            };
            await WaitForHealthAsync(http);
            using var created = await http.PostAsJsonAsync(
                "/api/v1/projects/initialize",
                new { rootPath = projectRoot });
            created.EnsureSuccessStatusCode();
            using var project = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            return project.RootElement.GetProperty("handle").GetString()
                ?? throw new InvalidOperationException("The daemon registered the project without a handle.");
        }

        private static async Task WaitForHealthAsync(HttpClient http)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
            while (DateTime.UtcNow < deadline)
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
                    // Not listening yet: the daemon is still starting.
                }

                await Task.Delay(100);
            }

            throw new TimeoutException("The daemon did not answer on its port within a minute.");
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static string Text(CallToolResult result) =>
            string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
