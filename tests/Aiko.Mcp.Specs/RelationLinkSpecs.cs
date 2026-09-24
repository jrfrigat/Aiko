using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A link can be taken back. Nothing could remove one: a mistaken "blocks A -> B" held B back for good unless
/// someone edited relations.json by hand, and retrying aiko_link_cards stored the same edge again.
/// </summary>
/// <remarks>Each spec works in a project of its own, made from the built-in template.</remarks>
public class RelationLinkSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task Linking_the_same_cards_twice_keeps_one_edge()
    {
        var (client, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var source = await CreateCardAsync(client);
        var target = await CreateCardAsync(client);

        var first = await LinkAsync(client, source, target, "blocks");
        var second = await LinkAsync(client, source, target, "blocks");

        Assert.Equal(Id(first), Id(second));
        Assert.Single(await LinksAsync(client));
    }

    [Fact]
    public async Task Unlinking_a_block_lets_the_blocked_card_start()
    {
        var (client, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var blocker = await CreateCardAsync(client);
        var blocked = await CreateCardAsync(client);
        var link = await LinkAsync(client, blocker, blocked, "blocks");
        Assert.True((await StartAsync(client, blocked)).IsError);

        var removed = await CallAsync(client, "aiko_unlink_cards", new() { ["relationId"] = Id(link) });

        Assert.NotEqual(true, removed.IsError);
        Assert.Empty(await LinksAsync(client));
        Assert.NotEqual(true, (await StartAsync(client, blocked)).IsError);
    }

    [Fact]
    public async Task A_link_can_be_named_by_its_ends_and_type_instead_of_its_id()
    {
        var (client, _) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var source = await CreateCardAsync(client);
        var target = await CreateCardAsync(client);
        await LinkAsync(client, source, target, "relates-to");

        var removed = await CallAsync(
            client,
            "aiko_unlink_cards",
            new() { ["sourceCardId"] = target, ["targetCardId"] = source, ["relationType"] = "relates-to" });

        Assert.NotEqual(true, removed.IsError);
        Assert.Empty(await LinksAsync(client));
        var again = await CallAsync(client, "aiko_unlink_cards", new() { ["relationId"] = "no-such-link" });
        Assert.True(again.IsError);
    }

    [Fact]
    public async Task The_board_removes_a_link_through_rest()
    {
        var (client, projectId) = await ConnectToNewProjectAsync();
        await using var owned = client;
        var link = await LinkAsync(client, await CreateCardAsync(client), await CreateCardAsync(client), "implements");
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };

        using var removed = await http.DeleteAsync($"api/v1/projects/{projectId}/relations/{Id(link)}");
        using var gone = await http.DeleteAsync($"api/v1/projects/{projectId}/relations/{Id(link)}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Empty(await LinksAsync(client));
    }

    private static Task<CallToolResult> LinkAsync(McpClient client, string source, string target, string type) =>
        CallAsync(
            client,
            "aiko_link_cards",
            new() { ["sourceCardId"] = source, ["targetCardId"] = target, ["relationType"] = type });

    private static Task<CallToolResult> StartAsync(McpClient client, string cardId) =>
        CallAsync(
            client,
            "aiko_start_stage",
            new() { ["cardId"] = cardId, ["stageId"] = "analysis", ["agentAdapterId"] = "claude-code" });

    private static async Task<JsonElement[]> LinksAsync(McpClient client)
    {
        var board = await CallAsync(client, "aiko_list_board", new());
        using var json = JsonDocument.Parse(Text(board));
        return [.. json.RootElement.GetProperty("relations").EnumerateArray().Select(element => element.Clone())];
    }

    private static string Id(CallToolResult link)
    {
        Assert.NotEqual(true, link.IsError);
        using var json = JsonDocument.Parse(Text(link));
        return json.RootElement.GetProperty("id").GetString()!;
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
            new() { ["kind"] = "task", ["title"] = "Link me", ["ownPriority"] = 1 });
        using var json = JsonDocument.Parse(Text(created));
        return json.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    private async Task<(McpClient Client, string ProjectId)> ConnectToNewProjectAsync()
    {
        var name = $"links-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.PostAsJsonAsync("/api/v1/projects/initialize", new { rootPath = root, name });
        response.EnsureSuccessStatusCode();
        var projectId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{projectId}"),
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);
        return (client, projectId);
    }
}
