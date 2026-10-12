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

    [Fact]
    public async Task A_creation_answers_with_a_compact_card()
    {
        await using var client = await ConnectToNewProjectAsync();

        // Five creations in a row, as a breakdown makes them: each answer has to stay the size of the id an
        // agent acts on, not the size of the requirements it just sent.
        for (var index = 0; index < 5; index++)
        {
            var created = await CallAsync(
                client,
                "aiko_create_card",
                new()
                {
                    ["kind"] = "task",
                    ["title"] = $"Breakdown {index}",
                    ["ownPriority"] = 1,
                    ["declaredScopeFiles"] = new[] { "src/**" },
                    ["requirements"] = new string('x', 4000)
                });

            Assert.NotEqual(true, created.IsError);
            var text = Text(created);
            using var answer = JsonDocument.Parse(text);

            // The answer is the card's identity next to the revision the next call must name - and nothing
            // else. The texts a creation took in are not echoed back, and the scope is not either.
            var keys = answer.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                ["kind", "reference", "revision", "stageId", "title", "workflowId"],
                keys);
            Assert.Equal(1, answer.RootElement.GetProperty("revision").GetInt64());
            Assert.Equal("backlog", answer.RootElement.GetProperty("stageId").GetString());
            Assert.StartsWith(
                "TASK-",
                answer.RootElement.GetProperty("reference").GetProperty("cardId").GetString(),
                StringComparison.Ordinal);
            // An answer an agent reads without spending its budget: one or two screens at most.
            Assert.True(text.Length < 512, $"The creation answer is {text.Length} characters: {text}");
        }
    }

    [Fact]
    public async Task A_creation_can_answer_with_the_whole_card()
    {
        await using var client = await ConnectToNewProjectAsync();

        var created = await CallAsync(
            client,
            "aiko_create_card",
            new()
            {
                ["kind"] = "task",
                ["title"] = "The whole card, please",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" },
                ["requirements"] = "Make the dropdown list every value.",
                ["fullCard"] = true
            });

        Assert.NotEqual(true, created.IsError);
        using var answer = JsonDocument.Parse(Text(created));

        // The option restores the whole card: the texts, the scope and the plan attribution a compact
        // answer leaves to aiko_get_card.
        Assert.Equal(
            "Make the dropdown list every value.",
            answer.RootElement.GetProperty("metadata").GetProperty("requirements").GetString());
        Assert.Equal(
            "src/**",
            answer.RootElement.GetProperty("declaredScopeFiles").EnumerateArray().Single().GetString());
        Assert.Equal("The whole card, please", answer.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_child_can_be_linked_to_its_parent_from_the_compact_answers()
    {
        await using var client = await ConnectToNewProjectAsync();
        var parent = await CreateCardIdAsync(client, "Parent of the child");
        var child = await CreateCardIdAsync(client, "Child of the parent");

        var linked = await CallAsync(
            client,
            "aiko_link_cards",
            new()
            {
                ["sourceCardId"] = parent,
                ["targetCardId"] = child,
                ["relationType"] = "parent-child"
            });
        Assert.NotEqual(true, linked.IsError);

        // The compact answer carried everything the link needed - the id and nothing more.
        using var board = JsonDocument.Parse(Text(await CallAsync(client, "aiko_list_board", new())));
        Assert.Contains(
            board.RootElement.GetProperty("relations").EnumerateArray(),
            relation =>
                relation.GetProperty("source").GetProperty("cardId").GetString() == parent
                && relation.GetProperty("target").GetProperty("cardId").GetString() == child
                && relation.GetProperty("type").GetString() == "parent-child");
    }

    [Fact]
    public async Task The_other_card_write_tools_answer_with_a_compact_card()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardIdAsync(client, "A card the write tools touch");

        // Each of these used to answer with the whole card - its requirements and all. What the next step
        // needs is the card's identity and its new revision, plus the small fields the tool is about.
        // Archive and restore share the same writer as each other and are covered by their own specs.
        var answers = new (string Tool, CallToolResult Result)[]
        {
            ("aiko_estimate_card", await CallAsync(
                client,
                "aiko_estimate_card",
                new()
                {
                    ["cardId"] = cardId,
                    ["expectedRevision"] = 1,
                    ["size"] = "S",
                    ["criterionValues"] = new[] { "app-point=3" }
                })),
            ("aiko_update_card", await CallAsync(
                client,
                "aiko_update_card",
                new()
                {
                    ["cardId"] = cardId,
                    ["expectedRevision"] = 2,
                    ["title"] = "Renamed by the spec",
                    ["ownPriority"] = 1,
                    ["declaredScopeFiles"] = Array.Empty<string>(),
                    ["actualChangedFiles"] = Array.Empty<string>()
                })),
            ("aiko_move_card", await CallAsync(
                client,
                "aiko_move_card",
                new() { ["cardId"] = cardId, ["stageId"] = "analysis", ["expectedRevision"] = 3 })),
            ("aiko_take_card", await CallAsync(
                client,
                "aiko_take_card",
                new() { ["cardId"] = cardId, ["agentAdapterId"] = "cline" }))
        };

        foreach (var (tool, result) in answers)
        {
            Assert.False(result.IsError == true, $"{tool} refused: {Text(result)}");
            var text = Text(result);
            using var answer = JsonDocument.Parse(text);
            Assert.Equal(
                cardId,
                answer.RootElement.GetProperty("reference").GetProperty("cardId").GetString());
            Assert.Equal(JsonValueKind.Number, answer.RootElement.GetProperty("revision").ValueKind);
            // The card's texts are the heavy part and the next step never needs them.
            Assert.False(
                answer.RootElement.TryGetProperty("metadata", out _),
                $"{tool} echoed the card's metadata: {text}");
            Assert.True(text.Length < 512, $"{tool} answered with {text.Length} characters: {text}");
        }
    }

    [Fact]
    public async Task The_artifact_and_comment_answers_are_compact()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardIdAsync(client, "A card that takes notes");

        // A document's content and a note's body are what the caller just sent. What the answer owes is the
        // version the next write must name, and the note's identity - not the kilobytes written back out.
        var saved = await CallAsync(
            client,
            "aiko_save_card_artifact",
            new() { ["cardId"] = cardId, ["path"] = "analysis.md", ["content"] = new string('x', 3000) });
        Assert.False(saved.IsError == true, Text(saved));
        using (var document = JsonDocument.Parse(Text(saved)))
        {
            Assert.Equal("analysis.md", document.RootElement.GetProperty("path").GetString());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("version").GetString()));
            Assert.False(document.RootElement.TryGetProperty("content", out _), Text(saved));
        }

        Assert.True(Text(saved).Length < 512, $"The artifact answer is {Text(saved).Length} characters");

        var commented = await CallAsync(
            client,
            "aiko_add_comment",
            new() { ["cardId"] = cardId, ["body"] = new string('y', 3000), ["author"] = "cline" });
        Assert.False(commented.IsError == true, Text(commented));
        using (var note = JsonDocument.Parse(Text(commented)))
        {
            Assert.False(string.IsNullOrWhiteSpace(note.RootElement.GetProperty("id").GetString()));
            Assert.False(note.RootElement.TryGetProperty("body", out _), Text(commented));
        }

        Assert.True(Text(commented).Length < 512, $"The comment answer is {Text(commented).Length} characters");

        // ... and the whole document is one flag away, for a caller that wants it immediately.
        var whole = await CallAsync(
            client,
            "aiko_save_card_artifact",
            new()
            {
                ["cardId"] = cardId,
                ["path"] = "notes.md",
                ["content"] = "# Whole document",
                ["fullDocument"] = true
            });
        Assert.False(whole.IsError == true, Text(whole));
        Assert.Contains("# Whole document", Text(whole), StringComparison.Ordinal);
    }

    private static async Task<CallToolResult> CallAsync(
        McpClient client,
        string tool,
        Dictionary<string, object?> arguments) =>
        await client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private static Task<string> CreateCardAsync(McpClient client) =>
        CreateCardIdAsync(client, "Answer me");

    /// <summary>Creates a card and reads its id out of the compact answer a creation gives by default.</summary>
    private static async Task<string> CreateCardIdAsync(McpClient client, string title)
    {
        var created = await CallAsync(
            client,
            "aiko_create_card",
            new() { ["kind"] = "task", ["title"] = title, ["ownPriority"] = 1 });
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
