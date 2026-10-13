using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A failure the daemon did not cause itself must not be described to the caller in the runtime's words: an
/// IOException names the path it could not touch, and a JsonException quotes what it could not read - the
/// machine's layout, handed to an agent or a page. What the daemon wrote itself is a different matter: an
/// unknown card, a refused start or a taken project id is the answer the caller needs, and it travels
/// (TASK-162).
/// </summary>
public sealed class ErrorTextSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    /// <summary>What a client gets instead of a message the daemon did not write.</summary>
    private const string SystemFailure = "The daemon could not carry out the request.";

    [Fact]
    public async Task A_system_failure_answers_in_general_terms_and_not_with_the_machines_layout()
    {
        // A directory that exists - so the daemon's own validation is satisfied - with a *file* where the
        // project's .aiko directory belongs: creating that directory then fails in the I/O layer, whose
        // message names the path it could not touch. The endpoint catches a missing directory and the
        // daemon's own validation, not an I/O failure, so this reaches the middleware that answers every API
        // route.
        var root = Path.Combine(Path.GetTempPath(), $"aiko-error-text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var blocker = Path.Combine(root, ".aiko");
        await File.WriteAllTextAsync(blocker, "not a directory");
        try
        {
            using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
            using var content = new StringContent(
                JsonSerializer.Serialize(new { rootPath = root }),
                Encoding.UTF8,
                "application/json");
            using var response = await http.PostAsync("/api/v1/projects/initialize", content);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(SystemFailure, body, StringComparison.Ordinal);
            Assert.DoesNotContain(root, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(blocker, body, StringComparison.OrdinalIgnoreCase);
            // No path at all, rather than only this one: the failure is about the request, not about a disk.
            Assert.DoesNotContain(":\\", body, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blocker);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_refusal_the_daemon_wrote_itself_still_names_what_it_refused()
    {
        var unknown = $"TASK-NOBODY-{Guid.NewGuid():N}";
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = fixture.McpEndpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        // A move of a card that does not exist: the store refuses it and the store's words are the answer an
        // agent acts on, so the rule above must not swallow them into the generic text.
        var result = await client.CallToolAsync(
            "aiko_move_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = unknown,
                ["stageId"] = "analysis",
                ["expectedRevision"] = 1
            },
            cancellationToken: CancellationToken.None);

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.IsError, $"An unknown card should be refused, not answered with '{text}'.");
        Assert.False(string.IsNullOrWhiteSpace(text), "The refusal should say something.");
        Assert.DoesNotContain(SystemFailure, text, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
    }
}
