using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The idempotency key a mutating tool takes: a call repeated with the same key is answered from the record
/// of the first one instead of leaving a second card or a second note behind.
/// </summary>
/// <remarks>
/// The fixture's project is shared with the specs beside this class, so every card here carries a title of
/// its own and every key is fresh: a spec that read the whole board would otherwise depend on which of its
/// neighbours ran first.
/// </remarks>
public class IdempotencySpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = fixture.McpEndpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        });

        return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
    }

    private static string? FirstText(CallToolResult result) =>
        result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault();

    /// <summary>A key no other call in this suite uses.</summary>
    private static string NewKey() => $"spec-{Guid.NewGuid():N}";

    /// <summary>A title no other card in the shared project carries.</summary>
    private static string NewTitle(string what) => $"Idempotency spec {what} {Guid.NewGuid():N}";

    private static async Task<JsonElement> CreateAsync(McpClient client, string title, string? requestId)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["kind"] = "task",
            ["title"] = title,
            ["ownPriority"] = 0
        };
        if (requestId is not null)
        {
            arguments["requestId"] = requestId;
        }

        var answer = await client.CallToolAsync(
            "aiko_create_card",
            arguments,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    /// <summary>The card id of a card answer: the contract names it inside the card's reference.</summary>
    private static string CardIdOf(JsonElement card) =>
        card.GetProperty("reference").GetProperty("cardId").GetString()!;

    private static async Task<JsonElement> CommentAsync(
        McpClient client,
        string cardId,
        string body,
        string? requestId)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["cardId"] = cardId,
            ["body"] = body,
            ["author"] = "idempotency-specs"
        };
        if (requestId is not null)
        {
            arguments["requestId"] = requestId;
        }

        var answer = await client.CallToolAsync(
            "aiko_add_comment",
            arguments,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    private static async Task<JsonElement> CommentsAsync(McpClient client, string cardId)
    {
        var answer = await client.CallToolAsync(
            "aiko_list_comments",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    [Fact]
    public async Task A_repeated_create_card_with_the_same_key_creates_one_card()
    {
        var client = await ConnectAsync();
        var key = NewKey();
        var title = NewTitle("create");

        var first = await CreateAsync(client, title, key);
        // The second call asks for something else on purpose: were it executed, the answer would differ.
        var repeated = await CreateAsync(client, $"{title} (repeated)", key);

        Assert.Equal(CardIdOf(first), CardIdOf(repeated));
        Assert.Equal(title, repeated.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_create_card_without_a_key_creates_a_second_card()
    {
        var client = await ConnectAsync();

        var first = await CreateAsync(client, NewTitle("plain"), requestId: null);
        var second = await CreateAsync(client, NewTitle("plain"), requestId: null);

        Assert.NotEqual(CardIdOf(first), CardIdOf(second));
    }

    [Fact]
    public async Task The_same_key_in_another_tool_names_another_call()
    {
        var client = await ConnectAsync();
        var key = NewKey();
        var card = await CreateAsync(client, NewTitle("scope"), key);

        // The key was spent on a card creation; a note under the same key must still be written.
        var comment = await CommentAsync(client, CardIdOf(card), "a note", key);

        Assert.Equal("a note", comment.GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_repeated_comment_with_the_same_key_writes_one_note()
    {
        var client = await ConnectAsync();
        var cardId = CardIdOf(await CreateAsync(client, NewTitle("comment"), requestId: null));
        var key = NewKey();

        var first = await CommentAsync(client, cardId, "the first note", key);
        var repeated = await CommentAsync(client, cardId, "the second note", key);

        Assert.Equal(first.GetProperty("id").GetString(), repeated.GetProperty("id").GetString());
        Assert.Equal("the first note", repeated.GetProperty("body").GetString());

        var comments = await CommentsAsync(client, cardId);
        Assert.Equal(1, comments.GetArrayLength());
    }
}
