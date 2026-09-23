using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// What cannot be written into a project's priority: a negative priority on a card, or a criterion no card
/// could be scored against. Either used to reach the calculator and take the whole board down with it; now the
/// write is refused with its reason, and the board keeps answering.
/// </summary>
public class PriorityGuardSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task A_negative_priority_is_refused_and_the_board_still_answers()
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = fixture.McpEndpoint,
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);

        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?> { ["kind"] = "task", ["title"] = "Priority guard", ["ownPriority"] = 1 },
            cancellationToken: CancellationToken.None);
        var cardId = JsonDocument.Parse(created.Content.OfType<TextContentBlock>().First().Text)
            .RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;

        var refused = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?> { ["cardId"] = cardId, ["expectedRevision"] = 1, ["ownPriority"] = "-1" },
            cancellationToken: CancellationToken.None);

        Assert.True(refused.IsError);
        Assert.Contains("negative", refused.Content.OfType<TextContentBlock>().First().Text, StringComparison.OrdinalIgnoreCase);

        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var board = await http.GetAsync($"api/v1/projects/{fixture.ProjectId}/board");
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
    }

    [Fact]
    public async Task Settings_with_a_negative_weight_are_refused_with_the_criterion_named()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        var settings = new
        {
            schemaVersion = 1,
            priority = new
            {
                weights = new { taskWeight = 0.6, parentWeight = 0.4 },
                criteria = new[] { new { id = "reach", title = "Reach", description = "", weight = -1, minimum = 0, maximum = 10 } }
            }
        };

        using var refused = await http.PutAsJsonAsync($"api/v1/projects/{fixture.ProjectId}/settings", settings);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("reach", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
