using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The release-plan surface an agent works through: reading the plans with their readiness, changing what a
/// version waits for, and closing a version's plan.
/// </summary>
/// <remarks>
/// The fixture's project is shared with the specs beside this class, so every plan written here carries a
/// version of its own: a spec that read the whole document would otherwise depend on which of its neighbours
/// ran first.
/// </remarks>
public class ReleasePlanSurfaceSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    /// <summary>A version nothing else in this suite uses, so the plan a spec writes is its own.</summary>
    private static string UniqueVersion(string part) => $"v8.{part}.{Guid.NewGuid().ToString("N")[..6]}";

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

    /// <summary>Changes a plan through the tool, which is the only writer there is.</summary>
    private static async Task<JsonElement> UpdateAsync(
        McpClient client,
        string version,
        string[]? addCards = null,
        string[]? removeCards = null,
        bool? isCurrent = null)
    {
        var arguments = new Dictionary<string, object?> { ["version"] = version };
        if (addCards is not null)
        {
            arguments["schemeId"] = "git-release";
            arguments["addCards"] = addCards;
        }

        if (removeCards is not null)
        {
            arguments["removeCards"] = removeCards;
        }

        if (isCurrent is not null)
        {
            arguments["isCurrent"] = isCurrent;
        }

        var answer = await client.CallToolAsync(
            "aiko_update_release_plan",
            arguments,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    /// <summary>The plan document as the tool answers it.</summary>
    private static async Task<JsonElement> ReadAsync(McpClient client)
    {
        var answer = await client.CallToolAsync(
            "aiko_list_release_plans",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
        return JsonDocument.Parse(FirstText(answer)!).RootElement.Clone();
    }

    /// <summary>The report of one version, which the document holds most recently created first.</summary>
    private static JsonElement ReportOf(JsonElement report, string version) =>
        report.GetProperty("plans")
            .EnumerateArray()
            .First(entry =>
                entry.GetProperty("plan").GetProperty("version").GetString() == version);

    /// <summary>A card the project actually has, so a planned id resolves to a live card.</summary>
    private static async Task<string> CreateCardAsync(McpClient client, string title)
    {
        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = "task",
                ["title"] = title,
                ["ownPriority"] = 1
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, created.IsError);
        return JsonDocument.Parse(FirstText(created)!).RootElement
            .GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    [Fact]
    public async Task A_plan_is_created_changed_and_read_back_with_its_readiness()
    {
        await using var client = await ConnectAsync();
        var cardId = await CreateCardAsync(client, "Planned for a version");
        var version = UniqueVersion("1");

        var plan = await UpdateAsync(client, version, addCards: [cardId, "TASK-99999"], isCurrent: true);
        Assert.Equal(version, plan.GetProperty("version").GetString());
        Assert.True(plan.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(2, plan.GetProperty("cards").GetArrayLength());

        var entry = ReportOf(await ReadAsync(client), version);
        var readiness = entry.GetProperty("readiness");

        // The card the project has is not finished, and its type's pipeline says so; the id no card carries is
        // reported as missing rather than as work in progress, which is the whole reason that state exists.
        var states = readiness.GetProperty("cards")
            .EnumerateArray()
            .ToDictionary(
                card => card.GetProperty("cardId").GetString()!,
                card => card.GetProperty("state").GetString()!);
        Assert.Equal("InWork", states[cardId]);
        Assert.Equal("Missing", states["TASK-99999"]);

        Assert.Equal(1, readiness.GetProperty("inWork").GetInt32());
        Assert.Equal(1, readiness.GetProperty("missing").GetInt32());
        Assert.Equal(0, readiness.GetProperty("finished").GetInt32());
        Assert.False(readiness.GetProperty("isReleasable").GetBoolean());
    }

    [Fact]
    public async Task Cards_are_added_and_removed_and_only_one_plan_is_current()
    {
        await using var client = await ConnectAsync();
        var first = await CreateCardAsync(client, "First planned card");
        var second = await CreateCardAsync(client, "Second planned card");
        var older = UniqueVersion("2");
        var newer = UniqueVersion("3");

        await UpdateAsync(client, older, addCards: [first, second], isCurrent: true);
        await UpdateAsync(client, newer, addCards: [first], isCurrent: true);
        await UpdateAsync(client, older, removeCards: [second]);

        var report = await ReadAsync(client);

        // The mark is single: making the newer plan current took it off the older one, so "where does a new card
        // go" has one answer.
        Assert.True(ReportOf(report, newer).GetProperty("plan").GetProperty("isCurrent").GetBoolean());
        Assert.False(ReportOf(report, older).GetProperty("plan").GetProperty("isCurrent").GetBoolean());

        var kept = ReportOf(report, older).GetProperty("plan").GetProperty("cards")
            .EnumerateArray()
            .Select(card => card.GetProperty("cardId").GetString())
            .ToArray();
        Assert.Single(kept);
        Assert.Equal(first, kept[0]);
    }

    [Fact]
    public async Task A_version_that_is_not_a_tag_is_refused_with_the_reason()
    {
        await using var client = await ConnectAsync();
        var refused = await client.CallToolAsync(
            "aiko_update_release_plan",
            new Dictionary<string, object?>
            {
                ["version"] = "0.2.0",
                ["schemeId"] = "git-release",
                ["addCards"] = new[] { "TASK-1" }
            },
            cancellationToken: CancellationToken.None);

        // The refusal reaches the agent with its reason, which is what lets it correct the version instead of
        // repeating the call that was refused.
        Assert.True(refused.IsError);
        Assert.Contains("git tag", FirstText(refused) ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_a_version_carries_the_work_that_outlived_it()
    {
        await using var client = await ConnectAsync();
        var cardId = await CreateCardAsync(client, "Outlives its version");
        var closing = UniqueVersion("4");
        var next = UniqueVersion("5");

        await UpdateAsync(client, closing, addCards: [cardId]);
        await UpdateAsync(client, next, addCards: [await CreateCardAsync(client, "Already planned next")]);

        // The receiving version is named rather than left to Aiko: which plan is "next" depends on what else the
        // project plans, and other specs share this project. The store's own choice is held by its specs.
        var closed = await client.CallToolAsync(
            "aiko_close_release_plan",
            new Dictionary<string, object?>
            {
                ["version"] = closing,
                ["cardsToCarry"] = new[] { cardId },
                ["carryIntoVersion"] = next
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, closed.IsError);
        var closedPlan = JsonDocument.Parse(FirstText(closed)!).RootElement;
        Assert.NotEqual(JsonValueKind.Null, closedPlan.GetProperty("releasedAt").ValueKind);
        // The carried card left the closed plan. The plan is not asserted to be empty: a card created later can
        // join the same plan through the inflow, and the claim this spec makes is about the carried card, not
        // about everything that ever entered the plan.
        Assert.DoesNotContain(
            cardId,
            closedPlan.GetProperty("cards")
                .EnumerateArray()
                .Select(card => card.GetProperty("cardId").GetString())
                .Where(id => id is not null)
                .Select(id => id!));

        // The card that outlived the version is in the next plan and says where it came from: a carry-over
        // nobody can see is exactly the silent loss the story forbids.
        var carried = ReportOf(await ReadAsync(client), next)
            .GetProperty("plan").GetProperty("cards")
            .EnumerateArray()
            .First(card => card.GetProperty("cardId").GetString() == cardId);
        Assert.Equal(closing, carried.GetProperty("carriedFromVersion").GetString());

        // And a closed plan refuses a further change: it is the answer to what that version was waiting for.
        var again = await client.CallToolAsync(
            "aiko_update_release_plan",
            new Dictionary<string, object?>
            {
                ["version"] = closing,
                ["removeCards"] = new[] { cardId }
            },
            cancellationToken: CancellationToken.None);
        Assert.True(again.IsError);
        Assert.Contains("closed", FirstText(again) ?? string.Empty, StringComparison.Ordinal);
    }
}
