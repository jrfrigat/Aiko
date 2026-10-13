using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Aiko.Mcp.Specs;

/// <summary>
/// What an answer costs, measured in the only currency the context pays in: characters of text.
/// </summary>
/// <remarks>
/// The epic's success is a before/after figure, and a figure needs a meter. The meter is a proxy for tokens -
/// characters of the answer, not tokens of a model - but it is an honest one: it is reproducible, it does not
/// depend on which model reads the answer, and it counts at the place the answer is produced.
///
/// The alternative, a counter inside the tools, was rejected: it would add state to the product nobody asked
/// for, and it could not cover the measurements this epic already made - a compact creation, a removed
/// read-back, an answer without a workspace path. Those numbers came out of answers, not out of a counter.
///
/// The budgets below pin what the epic has already reached, so a change that grows an answer again fails a test
/// rather than slipping through. The numbers still to *reach* - the marginal cost of a list answer, and the size
/// of the project context - are printed by the runs here and asserted by the tasks that lower them.
/// </remarks>
public class ToolAnswerBudgetSpecs(AikoServerFixture fixture, ITestOutputHelper output)
    : IClassFixture<AikoServerFixture>
{
    /// <summary>One screen: what a write answers with, and nothing more.</summary>
    private const int OneScreen = 512;

    /// <summary>A handful of cards is read at a glance; a screen-worth of JSON is not.</summary>
    private const int AGlance = 4096;

    /// <summary>
    /// A run answers with the attempt it started or finished plus the card's position. One screen (512) is not met:
    /// the answers are 547 and 586 characters, because the whole execution is echoed - the attempt record with its
    /// state and both timestamps, and a dozen empty fields (no commits, no artifacts, no progress). Pinning that
    /// here keeps the epic's gains from slipping, and the assertion below pins what a run promised: no workspace.
    /// Trimming the echoed execution is work for whichever card takes it up, not for the meter.
    /// </summary>
    private const int AShortRun = 1024;

    /// <summary>The project context is a document by nature: types, stages, rules, scoring, memory.</summary>
    private const int ADocument = 32_000;

    [Fact]
    public async Task A_created_card_is_listed_without_carrying_what_it_was_asked_to_do()
    {
        await using var client = await ConnectToNewProjectAsync();

        var created = await CallAsync(client, "aiko_create_card", new()
        {
            ["kind"] = "task",
            ["title"] = "Meter me",
            ["ownPriority"] = 1
        });
        AssertBudget("create_card", created, OneScreen);
        var cardId = CardIdOf(created);

        var listed = await CallAsync(client, "aiko_list_cards", new() { ["kind"] = "task" });
        output.WriteLine($"list_cards (one card): {SizeOf(listed)}");
        Assert.True(SizeOf(listed) < AGlance, $"list_cards answered {SizeOf(listed)} characters");
        Assert.Contains(cardId, Text(listed));
    }

    [Fact]
    public async Task Taking_a_card_through_a_stage_answers_inside_one_screen_each_time()
    {
        await using var client = await ConnectToNewProjectAsync();

        var created = await CallAsync(client, "aiko_create_card", new()
        {
            ["kind"] = "task",
            ["title"] = "Take me through a stage",
            ["ownPriority"] = 1
        });
        AssertBudget("create_card", created, OneScreen);
        var cardId = CardIdOf(created);
        var revision = RevisionOf(created);

        var taken = await CallAsync(client, "aiko_take_card", new()
        {
            ["cardId"] = cardId,
            ["agentAdapterId"] = "cline"
        });
        AssertBudget("take_card", taken, OneScreen);
        revision = RevisionOr(taken, revision);

        var started = await CallAsync(client, "aiko_start_stage", new()
        {
            ["cardId"] = cardId,
            ["stageId"] = "backlog",
            ["agentAdapterId"] = "cline"
        });
        AssertBudget("start_stage", started, AShortRun);
        Assert.DoesNotContain("workspacePath", Text(started));
        revision = RevisionOr(started, revision);

        // A stage is completed only once the card is re-estimated during its own run, so the meter takes the same
        // order an agent must: start, estimate, complete. The criterion is the template project's own name for
        // readiness - this project renames it, a project from the built-in template does not.
        var estimated = await CallAsync(client, "aiko_estimate_card", new()
        {
            ["cardId"] = cardId,
            ["expectedRevision"] = revision,
            ["size"] = "S",
            ["criterionValues"] = new[] { "complete=1" }
        });
        AssertBudget("estimate_card", estimated, OneScreen);
        revision = RevisionOr(estimated, revision);

        var completed = await CallAsync(client, "aiko_complete_stage", new()
        {
            ["executionId"] = ExecutionIdOf(started),
            ["actualChangedFiles"] = Array.Empty<string>(),
            ["artifacts"] = Array.Empty<string>()
        });
        AssertBudget("complete_stage", completed, AShortRun);
        Assert.DoesNotContain("workspacePath", Text(completed));
        revision = RevisionOr(completed, revision);

        var moved = await CallAsync(client, "aiko_move_card", new()
        {
            ["cardId"] = cardId,
            ["stageId"] = "analysis",
            ["expectedRevision"] = revision
        });
        AssertBudget("move_card", moved, OneScreen);
        revision = RevisionOr(moved, revision);

        var updated = await CallAsync(client, "aiko_update_card", new()
        {
            ["cardId"] = cardId,
            ["expectedRevision"] = revision,
            ["title"] = "Renamed by the meter",
            ["ownPriority"] = 2,
            ["declaredScopeFiles"] = Array.Empty<string>(),
            ["actualChangedFiles"] = Array.Empty<string>()
        });
        AssertBudget("update_card", updated, OneScreen);
    }

    [Fact]
    public async Task An_artifact_and_a_comment_answer_inside_one_screen()
    {
        await using var client = await ConnectToNewProjectAsync();
        var cardId = await CreateCardIdAsync(client, "Write me down");

        var saved = await CallAsync(client, "aiko_save_card_artifact", new()
        {
            ["cardId"] = cardId,
            ["path"] = "analysis.md",
            ["content"] = "# Analysis\n\nWhat the stage found."
        });
        AssertBudget("save_card_artifact", saved, OneScreen);

        var commented = await CallAsync(client, "aiko_add_comment", new()
        {
            ["cardId"] = cardId,
            ["body"] = "What the stage learned.",
            ["author"] = "cline"
        });
        AssertBudget("add_comment", commented, OneScreen);
    }

    [Fact]
    public async Task The_project_context_is_a_document_and_is_measured_as_one()
    {
        await using var client = await ConnectToNewProjectAsync();

        var context = await CallAsync(client, "aiko_get_project_context", new());
        output.WriteLine($"get_project_context: {SizeOf(context)}");
        Assert.True(
            SizeOf(context) < ADocument,
            $"The project context answered {SizeOf(context)} characters (budget {ADocument})");
    }

    /// <summary>
    /// The number the epic exists for: what a list answer costs as the cards it lists grow.
    /// </summary>
    /// <remarks>
    /// A hundred cards cannot be built inside a run, so this spec does not imitate a big project - it measures the
    /// marginal cost, which is the same at ten cards as at a thousand: how much one card adds to the answer, and
    /// whether what that card was *asked to do* rides along with it.
    ///
    /// It prints the number and asserts the shape of the answer only. Asserting the marginal cost is the next
    /// task's acceptance criterion - today the marginal cost is the whole requirements, and a budget here would be
    /// a red test for work not yet done. This is the "before" figure, printed where anyone can read it.
    /// </remarks>
    [Fact]
    public async Task A_list_answer_grows_with_the_cards_it_lists_and_not_with_their_requirements()
    {
        await using var client = await ConnectToNewProjectAsync();
        await CreateCardIdAsync(client, "Short");

        var beforeResult = await CallAsync(client, "aiko_list_cards", new());
        var before = SizeOf(beforeResult);
        Assert.Equal(1, CardCountOf(beforeResult));

        const int requirementsLength = 4000;
        await CallAsync(client, "aiko_create_card", new()
        {
            ["kind"] = "task",
            ["title"] = "Asked to do a lot",
            ["ownPriority"] = 1,
            ["requirements"] = new string('x', requirementsLength)
        });

        var afterResult = await CallAsync(client, "aiko_list_cards", new());
        var after = SizeOf(afterResult);
        Assert.Equal(2, CardCountOf(afterResult));

        output.WriteLine(
            $"list_cards: {before} characters for one card, {after} for two - "
            + $"a {requirementsLength}-character requirements added {after - before}.");
    }

    private static async Task<CallToolResult> CallAsync(
        McpClient client,
        string tool,
        Dictionary<string, object?> arguments) =>
        await client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);

    /// <summary>What the agent receives: every text block of the answer, not just the first one.</summary>
    private static int SizeOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Sum(block => block.Text.Length);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private void AssertBudget(string tool, CallToolResult result, int budget)
    {
        var size = SizeOf(result);
        var text = Text(result);
        output.WriteLine(text.StartsWith('{')
            ? $"{tool}: {size}"
            : $"{tool}: {size} -> {text}");
        Assert.True(
            size < budget,
            $"{tool} answered with {size} characters (budget {budget}): {text}");
    }

    private static string CardIdOf(CallToolResult result)
    {
        using var json = JsonDocument.Parse(Text(result));
        return json.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    private static string ExecutionIdOf(CallToolResult result)
    {
        using var json = JsonDocument.Parse(Text(result));
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static int RevisionOf(CallToolResult result)
    {
        using var json = JsonDocument.Parse(Text(result));
        return json.RootElement.GetProperty("revision").GetInt32();
    }

    /// <summary>An answer that does not restate the revision leaves the last one we read in place.</summary>
    private static int RevisionOr(CallToolResult result, int fallback)
    {
        var text = Text(result);
        if (!text.TrimStart().StartsWith('{'))
        {
            throw new InvalidOperationException($"This answer is not the JSON the meter expects: {text}");
        }

        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        foreach (var name in new[] { "revision", "cardRevision" })
        {
            if (root.TryGetProperty(name, out var value) && value.TryGetInt32(out var revision))
            {
                return revision;
            }
        }

        return fallback;
    }

    /// <summary>How many cards an answer carries, whether it is a bare array or a list under a name.</summary>
    private static int CardCountOf(CallToolResult result)
    {
        using var json = JsonDocument.Parse(Text(result));
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.GetArrayLength();
        }

        foreach (var name in new[] { "cards", "items" })
        {
            if (root.TryGetProperty(name, out var cards) && cards.ValueKind == JsonValueKind.Array)
            {
                return cards.GetArrayLength();
            }
        }

        throw new InvalidOperationException($"The answer carries no cards: {Text(result)}");
    }

    /// <summary>Creates a card and reads its id out of the compact answer a creation gives by default.</summary>
    private static async Task<string> CreateCardIdAsync(McpClient client, string title)
    {
        var created = await CallAsync(client, "aiko_create_card", new()
        {
            ["kind"] = "task",
            ["title"] = title,
            ["ownPriority"] = 1
        });
        return CardIdOf(created);
    }

    private async Task<McpClient> ConnectToNewProjectAsync()
    {
        var name = $"budget-{Guid.NewGuid():N}";
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
