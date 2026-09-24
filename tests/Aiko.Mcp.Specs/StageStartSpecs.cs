using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A start is a move, so it is held to the rules a move is: the stage must be one of the card's own pipeline and
/// admit its kind, it must be the stage the card is in or the next one, the stage must admit the agent, and a
/// card in the archive is off limits - through MCP and REST alike. A start used to write whatever stage id it was
/// handed: a typo took the card off the board, and starting 'done' stepped over the whole pipeline.
/// </summary>
/// <remarks>
/// Each spec works in a project of its own from the built-in template, whose task pipeline is backlog,
/// analysis, implementation, review, done.
/// </remarks>
public class StageStartSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task A_stage_the_pipeline_does_not_have_is_refused_and_the_card_stays_put()
    {
        var (client, _, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var cardId = await CreateCardAsync(client);

        var refused = await StartAsync(client, cardId, "analisys");

        Assert.True(refused.IsError);
        var reason = Text(refused);
        Assert.Contains("analisys", reason, StringComparison.Ordinal);
        Assert.Contains("analysis", reason, StringComparison.Ordinal);
        Assert.Equal("backlog", await StageOfAsync(client, cardId));
    }

    [Fact]
    public async Task A_start_cannot_step_over_a_stage()
    {
        var (client, _, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var cardId = await CreateCardAsync(client);

        var skipped = await StartAsync(client, cardId, "done");

        Assert.True(skipped.IsError);
        Assert.Contains("one stage at a time", Text(skipped), StringComparison.Ordinal);
        Assert.Equal("backlog", await StageOfAsync(client, cardId));
    }

    [Fact]
    public async Task A_stage_that_names_its_agents_refuses_the_others()
    {
        var (client, projectId, root) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var cardId = await CreateCardAsync(client);

        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        var workflow = ReadWorkflow(root);
        var analysis = workflow["stages"]!.AsArray()
            .Single(stage => (string?)stage!["id"] == "analysis")!;
        analysis["allowedAgentAdapterIds"] = new JsonArray("codex");
        analysis["defaultAgentAdapterId"] = "codex";
        using (var saved = await PutWorkflowAsync(http, projectId, workflow))
        {
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        }

        var refused = await StartAsync(client, cardId, "analysis");

        Assert.True(refused.IsError);
        Assert.Contains("codex", Text(refused), StringComparison.Ordinal);
        Assert.NotEqual(true, (await StartAsync(client, cardId, "analysis", "codex")).IsError);
    }

    [Fact]
    public async Task The_interface_is_refused_a_start_the_agent_is_refused()
    {
        var (client, projectId, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var cardId = await CreateCardAsync(client);

        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.PostAsJsonAsync(
            $"api/v1/projects/{projectId}/cards/{cardId}/executions",
            new { stageId = "review", agentAdapterId = "claude-code" });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("one stage at a time", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pipeline_cannot_drop_a_stage_that_still_has_an_open_run()
    {
        var (client, projectId, root) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var cardId = await CreateCardAsync(client);
        Assert.NotEqual(true, (await StartAsync(client, cardId, "analysis")).IsError);

        // The card is pulled back while its analysis stays open, so no card sits in the stage - only the run.
        var moved = await client.CallToolAsync(
            "aiko_move_card",
            new Dictionary<string, object?> { ["cardId"] = cardId, ["stageId"] = "backlog", ["expectedRevision"] = 2 },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, moved.IsError);

        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        var workflow = ReadWorkflow(root);
        var stages = workflow["stages"]!.AsArray();
        stages.Remove(stages.Single(stage => (string?)stage!["id"] == "analysis"));
        using var refused = await PutWorkflowAsync(http, projectId, workflow);

        Assert.False(refused.IsSuccessStatusCode);
        var reason = await refused.Content.ReadAsStringAsync();
        Assert.Contains("analysis", reason, StringComparison.Ordinal);
        Assert.Contains(cardId, reason, StringComparison.Ordinal);
    }

    private static JsonObject ReadWorkflow(string projectRoot) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot, ".aiko", "workflows", "task.json")))!.AsObject();

    private static Task<HttpResponseMessage> PutWorkflowAsync(HttpClient http, string projectId, JsonObject workflow) =>
        http.PutAsJsonAsync(
            $"api/v1/projects/{projectId}/workflows/task",
            new JsonObject
            {
                ["title"] = workflow["title"]!.DeepClone(),
                ["stages"] = workflow["stages"]!.DeepClone(),
                ["expectedRevision"] = workflow["revision"]!.DeepClone()
            });

    private static Task<CallToolResult> StartAsync(
        McpClient client,
        string cardId,
        string stageId,
        string agent = "claude-code") =>
        client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?> { ["cardId"] = cardId, ["stageId"] = stageId, ["agentAdapterId"] = agent },
            cancellationToken: CancellationToken.None).AsTask();

    private static async Task<string> StageOfAsync(McpClient client, string cardId)
    {
        var card = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        using var json = JsonDocument.Parse(Text(card));
        return json.RootElement.GetProperty("stageId").GetString()!;
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private static async Task<string> CreateCardAsync(McpClient client)
    {
        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?> { ["kind"] = "task", ["title"] = "Start me", ["ownPriority"] = 1 },
            cancellationToken: CancellationToken.None);
        using var json = JsonDocument.Parse(Text(created));
        return json.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    private async Task<(McpClient Client, string ProjectId, string Root)> ConnectToNewProjectAsync()
    {
        var name = $"start-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var content = JsonContent.Create(new { rootPath = root, name });
        using var response = await http.PostAsync("/api/v1/projects/initialize", content);
        response.EnsureSuccessStatusCode();
        var initialized = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = initialized.GetProperty("id").GetString()!;

        var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{projectId}"),
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);
        return (client, projectId, root);
    }
}
