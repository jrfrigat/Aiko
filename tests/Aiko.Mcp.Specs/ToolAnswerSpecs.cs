using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// What a tool answers is enough to take the next step. A start, a completion or a scope request changes the
/// card's revision, and the execution tools used to return the execution alone: the next estimate or move then
/// named the old revision and got a bare "expected N, actual N+1". Refusals now say what to do next.
/// </summary>
/// <remarks>Each spec works in a project of its own, made from the built-in template.</remarks>
public class ToolAnswerSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task A_start_answers_with_the_revision_and_stage_the_card_now_has()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var started = await CallAsync(
            client,
            "aiko_start_stage",
            new() { ["cardId"] = cardId, ["stageId"] = "analysis", ["agentAdapterId"] = "claude-code" });

        Assert.NotEqual(true, started.IsError);
        using var answer = JsonDocument.Parse(Text(started));
        using var card = JsonDocument.Parse(Text(await CallAsync(client, "aiko_get_card", new() { ["cardId"] = cardId })));
        Assert.Equal(
            card.RootElement.GetProperty("revision").GetInt64(),
            answer.RootElement.GetProperty("cardRevision").GetInt64());
        Assert.Equal("analysis", answer.RootElement.GetProperty("cardStageId").GetString());
    }

    [Fact]
    public async Task A_stale_revision_is_refused_with_the_way_to_the_current_one()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var refused = await CallAsync(
            client,
            "aiko_estimate_card",
            new() { ["cardId"] = cardId, ["expectedRevision"] = 7, ["size"] = "S" });

        Assert.True(refused.IsError);
        var reason = Text(refused);
        Assert.Contains("aiko_get_card", reason, StringComparison.Ordinal);
        Assert.Contains("retry with revision 1", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creating_a_card_under_a_taken_id_says_the_card_exists()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var refused = await CallAsync(
            client,
            "aiko_create_card",
            new() { ["cardId"] = cardId, ["kind"] = "task", ["title"] = "Twice", ["ownPriority"] = 1 });

        Assert.True(refused.IsError);
        Assert.Contains("already exists", Text(refused), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_artifact_conflict_names_both_versions_and_the_next_step()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);
        var saved = await CallAsync(
            client,
            "aiko_save_card_artifact",
            new() { ["cardId"] = cardId, ["path"] = "notes.md", ["content"] = "# First" });
        using var first = JsonDocument.Parse(Text(saved));
        var version = first.RootElement.GetProperty("version").GetString()!;

        var refused = await CallAsync(
            client,
            "aiko_save_card_artifact",
            new() { ["cardId"] = cardId, ["path"] = "notes.md", ["content"] = "# Again" });

        Assert.True(refused.IsError);
        var reason = Text(refused);
        Assert.Contains(version, reason, StringComparison.Ordinal);
        Assert.Contains("aiko_get_card_artifact", reason, StringComparison.Ordinal);
    }

    private static async Task<CallToolResult> CallAsync(
        McpClient client,
        string tool,
        Dictionary<string, object?> arguments) =>
        await client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private static async Task<string> CreateCardAsync(McpClient client)
    {
        var created = await CallAsync(
            client,
            "aiko_create_card",
            new() { ["kind"] = "task", ["title"] = "Answer me", ["ownPriority"] = 1 });
        using var json = JsonDocument.Parse(Text(created));
        return json.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    private async Task<McpClient> ConnectToNewProjectAsync()
    {
        var name = $"answers-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.PostAsJsonAsync("/api/v1/projects/initialize", new { rootPath = root, name });
        response.EnsureSuccessStatusCode();
        var projectId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        return await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{projectId}"),
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);
    }
}
