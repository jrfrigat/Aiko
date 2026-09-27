using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The inflow of new cards into the version being worked on: a card made while a version is planned joins that
/// version's plan, unless the caller says otherwise - and saying otherwise means saying why.
/// </summary>
/// <remarks>
/// The fixture's project is shared with the specs beside this class, so every version written here is its own.
/// The one case this suite cannot hold is "the project plans no version at all": whether a plan is current is
/// the project's shared state, and another spec in the suite sets one. That path is covered where it can be
/// isolated - the service's own behaviour - and the named-version case below holds the other half of the rule.
/// </remarks>
public class ReleasePlanInflowSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    private static string UniqueVersion(string part) => $"v7.{part}.{Guid.NewGuid().ToString("N")[..6]}";

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

    /// <summary>Makes a version the one new cards flow into.</summary>
    private static async Task PlanAsync(McpClient client, string version)
    {
        var answer = await client.CallToolAsync(
            "aiko_update_release_plan",
            new Dictionary<string, object?>
            {
                ["version"] = version,
                ["schemeId"] = "git-release",
                ["isCurrent"] = true
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, answer.IsError);
    }

    private static async Task<CallToolResult> CreateAsync(
        McpClient client,
        string title,
        string? releasePlan = null,
        string? reason = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["kind"] = "task",
            ["title"] = title,
            ["ownPriority"] = 1
        };
        if (releasePlan is not null)
        {
            arguments["releasePlan"] = releasePlan;
        }

        if (reason is not null)
        {
            arguments["releasePlanReason"] = reason;
        }

        return await client.CallToolAsync(
            "aiko_create_card",
            arguments,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>The card a creation answered with.</summary>
    private static JsonElement Created(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(FirstText(result)!).RootElement.Clone();
    }

    /// <summary>The plan of one version as the plan tool answers it.</summary>
    private static async Task<JsonElement> PlanOfAsync(McpClient client, string version)
    {
        var listed = await client.CallToolAsync(
            "aiko_list_release_plans",
            new Dictionary<string, object?>(),
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, listed.IsError);
        return JsonDocument.Parse(FirstText(listed)!).RootElement
            .GetProperty("plans")
            .EnumerateArray()
            .First(entry => entry.GetProperty("plan").GetProperty("version").GetString() == version)
            .GetProperty("plan");
    }

    [Fact]
    public async Task A_card_created_while_a_version_is_planned_joins_it()
    {
        await using var client = await ConnectAsync();
        var version = UniqueVersion("1");
        await PlanAsync(client, version);

        var card = Created(await CreateAsync(client, "Grew out of the version"));

        // The card says which version took it, and the plan really names it: the metadata mirrors the plan
        // rather than claiming something about it.
        Assert.Equal(
            version,
            card.GetProperty("metadata").GetProperty("release-plan").GetString());
        var cardId = card.GetProperty("reference").GetProperty("cardId").GetString();
        var planned = (await PlanOfAsync(client, version))
            .GetProperty("cards")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("cardId").GetString())
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        Assert.Contains(cardId!, planned);
    }

    [Fact]
    public async Task A_card_kept_out_of_the_release_has_to_say_why()
    {
        await using var client = await ConnectAsync();
        var version = UniqueVersion("2");
        await PlanAsync(client, version);

        // Without a reason the creation is refused: leaving work out of a release is a decision somebody has to
        // be able to read, so it cannot be taken silently.
        var refused = await CreateAsync(client, "Out of the release, silently", releasePlan: "none");
        Assert.True(refused.IsError);

        const string Because = "A tidy-up nobody asked for; it does not hold the version.";
        var card = Created(await CreateAsync(
            client, "Out of the release, deliberately", releasePlan: "none", reason: Because));

        Assert.Equal(
            Because,
            card.GetProperty("metadata").GetProperty("release-plan-exclusion").GetString());
        Assert.False(card.GetProperty("metadata").TryGetProperty("release-plan", out _));

        // And the plan did not take it either.
        var planned = (await PlanOfAsync(client, version))
            .GetProperty("cards")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("cardId").GetString())
            .Where(id => id is not null)
            .Select(id => id!)
            .ToArray();
        Assert.DoesNotContain(
            card.GetProperty("reference").GetProperty("cardId").GetString() ?? string.Empty,
            planned);
    }

    [Fact]
    public async Task A_version_nobody_planned_is_reported_rather_than_invented()
    {
        await using var client = await ConnectAsync();
        var version = UniqueVersion("3");

        var card = Created(await CreateAsync(client, "For a version nobody planned", releasePlan: version));

        // The card is created and the answer says what happened: a plan is not invented for a version nobody
        // planned, because that version's scheme is not Aiko's to guess.
        var note = card.GetProperty("metadata").GetProperty("release-plan-note").GetString() ?? string.Empty;
        Assert.Contains(version, note, StringComparison.Ordinal);
        Assert.Contains("aiko_update_release_plan", note, StringComparison.Ordinal);
    }
}
