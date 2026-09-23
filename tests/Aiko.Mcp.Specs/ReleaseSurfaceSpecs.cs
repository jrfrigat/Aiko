using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The release surfaces an agent and a screen work through: the history over HTTP, one release by version,
/// the release a card went into, and the tools that read and write the record.
/// </summary>
/// <remarks>
/// The fixture's project is shared with the specs beside this class, so every record written here carries a
/// version of its own: a spec that counted the whole history would otherwise depend on which of its
/// neighbours ran first.
/// </remarks>
public class ReleaseSurfaceSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    /// <summary>A tag nothing else in this suite uses, so the record a spec writes is its own.</summary>
    private static string UniqueVersion(string part) => $"v9.{part}.{Guid.NewGuid().ToString("N")[..6]}";

    private HttpClient CreateClient() => new() { BaseAddress = fixture.BaseUrl };

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

    /// <summary>
    /// Records a release through the tool, which is the only writer there is: a release is recorded by the
    /// agent that conducted it, and the HTTP surface has no route for it.
    /// </summary>
    private static async Task RecordAsync(
        McpClient client,
        string version,
        string schemeId,
        string[] cards,
        string? notes = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["schemeId"] = schemeId,
            ["cards"] = cards
        };
        if (notes is not null)
        {
            arguments["notes"] = notes;
        }

        var recorded = await client.CallToolAsync(
            "aiko_record_release",
            arguments,
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, recorded.IsError);
    }

    /// <summary>The history as version and card count, in the order the route answered.</summary>
    private static (string Version, int Cards)[] History(JsonElement history) =>
        [.. history.EnumerateArray().Select(entry => (
            entry.GetProperty("version").GetString()!,
            entry.GetProperty("cards").GetInt32()))];

    [Fact]
    public async Task The_history_answers_newest_first_and_one_release_comes_with_its_cards()
    {
        await using var client = await ConnectAsync();
        using var http = CreateClient();

        var older = UniqueVersion("1");
        var newer = UniqueVersion("2");
        await RecordAsync(client, older, "git-release", ["TASK-591", "TASK-592"], "First of the pair.");
        await RecordAsync(client, newer, "git-pre-release", []);

        var history = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/projects/{fixture.ProjectId}/releases");
        var entries = History(history);

        // Newest first: the release recorded last stands above the one recorded before it, and a line of the
        // history says how many cards went in rather than which ones.
        var newerAt = Array.FindIndex(entries, entry => entry.Version == newer);
        var olderAt = Array.FindIndex(entries, entry => entry.Version == older);
        Assert.True(newerAt >= 0 && olderAt >= 0, "Both recorded releases should be in the history.");
        Assert.True(newerAt < olderAt, "The release recorded last should come first.");
        Assert.Equal(0, entries[newerAt].Cards);
        Assert.Equal(2, entries[olderAt].Cards);

        // One release, with its cards and its note.
        var single = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/projects/{fixture.ProjectId}/releases/{older}");
        Assert.Equal(older, single.GetProperty("version").GetString());
        Assert.Equal("git-release", single.GetProperty("schemeId").GetString());
        Assert.Equal("First of the pair.", single.GetProperty("notes").GetString());
        Assert.Equal(
            new[] { "TASK-591", "TASK-592" },
            single.GetProperty("cards").EnumerateArray().Select(card => card.GetString()).ToArray());

        // A version nobody recorded is a 404 and not an empty record: "there is no such release" and "there is
        // one and it carried nothing" are different answers.
        var missing = await http.GetAsync($"/api/v1/projects/{fixture.ProjectId}/releases/{older}-nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_card_finds_its_release_and_one_in_no_release_is_not_found()
    {
        await using var client = await ConnectAsync();
        using var http = CreateClient();

        var version = UniqueVersion("3");
        await RecordAsync(client, version, "git-release", ["TASK-593"]);

        var released = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/projects/{fixture.ProjectId}/cards/TASK-593/release");
        Assert.Equal(version, released.GetProperty("version").GetString());
        Assert.Contains(
            "TASK-593",
            released.GetProperty("cards").EnumerateArray().Select(card => card.GetString()));

        // A card no release named is a 404, not an empty object: with an explicit list the answer is either
        // there or it is not there.
        var nowhere = await http.GetAsync($"/api/v1/projects/{fixture.ProjectId}/cards/TASK-594/release");
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [Fact]
    public async Task One_release_carries_the_titles_of_the_cards_it_can_still_find()
    {
        await using var client = await ConnectAsync();
        using var http = CreateClient();

        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = "task",
                ["title"] = "Release page shows card titles",
                ["ownPriority"] = 1
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, created.IsError);
        var cardId = JsonDocument.Parse(FirstText(created)!).RootElement
            .GetProperty("reference").GetProperty("cardId").GetString()!;

        var version = UniqueVersion("5");
        await RecordAsync(client, version, "git-release", [cardId, "TASK-99999"]);

        // The record keeps ids only; the title is read from the live card, and an id nobody can find stays a
        // bare id instead of failing the page.
        foreach (var route in new[]
                 {
                     $"/api/v1/projects/{fixture.ProjectId}/releases/{version}",
                     $"/api/v1/projects/{fixture.ProjectId}/cards/{cardId}/release"
                 })
        {
            var release = await http.GetFromJsonAsync<JsonElement>(route);
            var titles = release.GetProperty("cardTitles");
            Assert.Equal("Release page shows card titles", titles.GetProperty(cardId).GetString());
            Assert.False(titles.TryGetProperty("TASK-99999", out _));
            Assert.Equal(2, release.GetProperty("cards").GetArrayLength());
        }
    }

    [Fact]
    public async Task One_version_keeps_one_record_and_recording_it_again_is_refused()
    {
        await using var client = await ConnectAsync();
        using var http = CreateClient();

        var version = UniqueVersion("4");
        await RecordAsync(client, version, "git-release", ["TASK-595"]);

        var again = await client.CallToolAsync(
            "aiko_record_release",
            new Dictionary<string, object?>
            {
                ["version"] = version,
                ["schemeId"] = "git-release",
                ["cards"] = new[] { "TASK-596" }
            },
            cancellationToken: CancellationToken.None);

        // The refusal reaches the agent with its reason, which is what the tool filter exists for: an agent
        // that reads "already recorded" can tell the person instead of recording a second answer.
        Assert.True(again.IsError);
        Assert.Contains(version, FirstText(again) ?? string.Empty, StringComparison.Ordinal);

        // And the history still holds one record for that version, with the cards of the first one.
        var history = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/projects/{fixture.ProjectId}/releases");
        Assert.Single(History(history), entry => entry.Version == version);
        var kept = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/projects/{fixture.ProjectId}/releases/{version}");
        Assert.Equal(
            new[] { "TASK-595" },
            kept.GetProperty("cards").EnumerateArray().Select(card => card.GetString()).ToArray());
    }
}
