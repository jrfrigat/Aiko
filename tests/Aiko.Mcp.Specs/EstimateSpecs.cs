using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// What an estimate writes: the scores it names, on top of the ones the card already carries, and only for
/// criteria the project defines, inside each criterion's range. An estimate used to replace every score with
/// the ones it named - so re-scoring readiness alone, which is what closing a stage asks for, wiped the rest -
/// and it stored a misspelt id or an impossible value without a word, which also satisfied the stage gate.
/// </summary>
/// <remarks>
/// Each spec works in a project of its own, made from the built-in template, so its criteria are the
/// standard ones (app-point, user-point, complete - each 0..10) whatever the other specs do to theirs.
/// </remarks>
public class EstimateSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task An_estimate_keeps_the_scores_it_does_not_name()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var first = await EstimateAsync(client, cardId, 1, "app-point=3", "user-point=4", "complete=1");
        Assert.NotEqual(true, first.IsError);

        var second = await EstimateAsync(client, cardId, 2, "complete=6");
        Assert.NotEqual(true, second.IsError);

        var values = Scores(second);
        Assert.Equal(3m, values["app-point"]);
        Assert.Equal(4m, values["user-point"]);
        Assert.Equal(6m, values["complete"]);
    }

    [Fact]
    public async Task An_estimate_asked_to_replace_the_scores_keeps_only_the_ones_it_names()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);
        await EstimateAsync(client, cardId, 1, "app-point=3", "user-point=4", "complete=1");

        var replaced = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["expectedRevision"] = 2,
                ["criterionValues"] = new[] { "complete=6" },
                ["replaceScores"] = true
            },
            cancellationToken: CancellationToken.None);

        Assert.NotEqual(true, replaced.IsError);
        var values = Scores(replaced);
        Assert.Equal(6m, Assert.Single(values).Value);
    }

    [Fact]
    public async Task A_score_for_a_criterion_the_project_does_not_define_is_refused_with_the_ones_it_does()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var refused = await EstimateAsync(client, cardId, 1, "completed=10");

        Assert.True(refused.IsError);
        var reason = Text(refused);
        Assert.Contains("completed", reason, StringComparison.Ordinal);
        Assert.Contains("app-point, user-point, complete", reason, StringComparison.Ordinal);

        // Nothing was written, so the card still reads as never estimated.
        var card = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        using var json = JsonDocument.Parse(Text(card));
        Assert.Equal(1, json.RootElement.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task A_score_outside_its_criterion_range_is_refused_with_the_range()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardAsync(client);

        var refused = await EstimateAsync(client, cardId, 1, "complete=999");

        Assert.True(refused.IsError);
        var reason = Text(refused);
        Assert.Contains("complete", reason, StringComparison.Ordinal);
        Assert.Contains("0..10", reason, StringComparison.Ordinal);
    }

    private static Task<CallToolResult> EstimateAsync(
        McpClient client,
        string cardId,
        long expectedRevision,
        params string[] scores) =>
        client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["expectedRevision"] = expectedRevision,
                ["criterionValues"] = scores
            },
            cancellationToken: CancellationToken.None).AsTask();

    private static Dictionary<string, decimal> Scores(CallToolResult result)
    {
        using var json = JsonDocument.Parse(Text(result));
        return json.RootElement
            .GetProperty("criterionValues")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetDecimal(), StringComparer.Ordinal);
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private static async Task<string> CreateCardAsync(McpClient client)
    {
        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?> { ["kind"] = "task", ["title"] = "Estimate me", ["ownPriority"] = 1 },
            cancellationToken: CancellationToken.None);
        using var json = JsonDocument.Parse(Text(created));
        return json.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    private async Task<McpClient> ConnectToNewProjectAsync()
    {
        var name = $"estimate-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var content = JsonContent.Create(new { rootPath = root, name });
        using var response = await http.PostAsync("/api/v1/projects/initialize", content);
        response.EnsureSuccessStatusCode();
        var initialized = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = initialized.GetProperty("id").GetString()!;

        return await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{projectId}"),
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);
    }
}
