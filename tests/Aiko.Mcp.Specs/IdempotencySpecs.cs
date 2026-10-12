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

    private static async Task<JsonElement> EstimateAsync(
        McpClient client,
        string cardId,
        long expectedRevision,
        string size,
        string requestId)
    {
        var answer = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["expectedRevision"] = expectedRevision,
                ["size"] = size,
                ["requestId"] = requestId
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    private static async Task<JsonElement> GetCardAsync(McpClient client, string cardId)
    {
        var answer = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    private static async Task<JsonElement> SaveArtifactAsync(
        McpClient client,
        string cardId,
        string content,
        string requestId)
    {
        var answer = await client.CallToolAsync(
            "aiko_save_card_artifact",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["path"] = "analysis.md",
                ["content"] = content,
                ["requestId"] = requestId
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    private static async Task<JsonElement> CreateInProjectAsync(
        McpClient client,
        string projectId,
        string title,
        string? requestId)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["kind"] = "task",
            ["title"] = title,
            ["ownPriority"] = 0
        };
        if (requestId is not null)
        {
            arguments["requestId"] = requestId;
        }

        var answer = await client.CallToolAsync(
            "aiko_create_card_in_project",
            arguments,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

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

    [Fact]
    public async Task A_repeated_estimate_with_the_same_key_estimates_once()
    {
        var client = await ConnectAsync();
        var card = await CreateAsync(client, NewTitle("estimate"), requestId: null);
        var cardId = CardIdOf(card);
        var revision = card.GetProperty("revision").GetInt64();
        var key = NewKey();

        var first = await EstimateAsync(client, cardId, revision, "M", key);
        // The repeat names the same revision on purpose: were it executed it would be a revision conflict.
        var repeated = await EstimateAsync(client, cardId, revision, "L", key);

        Assert.Equal("M", first.GetProperty("size").GetString());
        Assert.Equal("M", repeated.GetProperty("size").GetString());
        Assert.Equal(first.GetProperty("revision").GetInt64(), repeated.GetProperty("revision").GetInt64());
        // The card did not move on either: the repeat answered from the record, it did not estimate again.
        var stored = await GetCardAsync(client, cardId);
        Assert.Equal(first.GetProperty("revision").GetInt64(), stored.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task A_repeated_artifact_write_with_the_same_key_writes_one_document()
    {
        var client = await ConnectAsync();
        var cardId = CardIdOf(await CreateAsync(client, NewTitle("artifact"), requestId: null));
        var key = NewKey();

        var first = await SaveArtifactAsync(client, cardId, "the first document", key);
        var repeated = await SaveArtifactAsync(client, cardId, "the second document", key);

        Assert.Equal(first.GetProperty("version").GetString(), repeated.GetProperty("version").GetString());
        Assert.Equal("the first document", repeated.GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_repeated_create_card_in_project_with_the_same_key_files_one_card()
    {
        var client = await ConnectAsync();
        var key = NewKey();
        var title = NewTitle("in project");

        var first = await CreateInProjectAsync(client, fixture.ProjectId, title, key);
        var repeated = await CreateInProjectAsync(client, fixture.ProjectId, $"{title} (repeated)", key);

        Assert.Equal(CardIdOf(first), CardIdOf(repeated));
        Assert.Equal(title, repeated.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_create_card_in_project_without_a_key_files_a_second_card()
    {
        var client = await ConnectAsync();

        var first = await CreateInProjectAsync(client, fixture.ProjectId, NewTitle("in project"), requestId: null);
        var second = await CreateInProjectAsync(client, fixture.ProjectId, NewTitle("in project"), requestId: null);

        Assert.NotEqual(CardIdOf(first), CardIdOf(second));
    }

    [Fact]
    public async Task Two_calls_at_once_with_one_key_create_one_card()
    {
        // Two clients, so the two attempts are two requests rather than one retried on the same connection.
        var first = await ConnectAsync();
        var second = await ConnectAsync();
        var key = NewKey();
        var title = NewTitle("at once");

        var answers = await Task.WhenAll(
            CreateAsync(first, title, key),
            CreateAsync(second, $"{title} (the other copy)", key));

        // Which of the two attempts takes the key first is a race, so either result may be the one both
        // answers carry - what must not happen is two cards.
        Assert.Equal(CardIdOf(answers[0]), CardIdOf(answers[1]));
        Assert.Equal(answers[0].GetProperty("title").GetString(), answers[1].GetProperty("title").GetString());
    }
}
