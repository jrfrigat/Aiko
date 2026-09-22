using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Aiko.Pwa.Resources;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the release history on the screen: where it is drawn, where one release is opened, where the card
/// page links to, and that every caption the new markup asks for exists in both dictionaries.
/// </summary>
/// <remarks>
/// The screens are read as text, the way <see cref="ReleasePageSpecs"/> reads them: the parts worth pinning
/// are the address, the caption and the condition - the things a refactor moves without a compiler noticing.
/// The caption check is derived from the markup itself, so a key typed into a page and forgotten in the
/// dictionaries fails here rather than as a blank word on somebody's screen.
/// </remarks>
public sealed class ReleaseHistoryScreenSpecs
{
    private static readonly ResourceManager Manager = new("Aiko.Pwa.Resources.Loc", typeof(Loc).Assembly);

    [Fact]
    public void The_release_screen_draws_the_history_and_marks_a_preliminary_release()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // The history is read from the route TASK-133 added, addressed by the handle the page was opened with.
        Assert.Contains(
            "api/v1/projects/{Uri.EscapeDataString(Handle)}/releases",
            page,
            StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseHistoryTitle\")", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ReleaseHistoryCards\", entry.Cards)", page, StringComparison.Ordinal);
        // An empty history is said in words rather than left as a gap.
        Assert.Contains("Loc.Get(\"ReleaseHistoryEmpty\")", page, StringComparison.Ordinal);

        // Every line leads to the release itself, and the address is built where addresses are built.
        Assert.Contains("ProjectRoutes.ReleaseRecord(Handle, entry.Version)", page, StringComparison.Ordinal);

        // Ordinary or preliminary comes from the tag's suffix, read from the one declaration of it.
        Assert.Contains("ReleaseSchemes.PreReleaseSuffix", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(IsPreliminary(entry.Version)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_record_answers_at_its_own_address_and_links_its_tasks()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleaseRecordPage.razor");

        Assert.Contains("@page \"/p/{Handle}/release/{Version}\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "api/v1/projects/{Uri.EscapeDataString(Handle)}/releases/{Uri.EscapeDataString(Version)}",
            page,
            StringComparison.Ordinal);
        // The tasks are links to their cards, built by the one helper that builds card addresses.
        Assert.Contains("ProjectRoutes.Card(Handle, card)", page, StringComparison.Ordinal);
        // And a release that carried nothing says so instead of showing an empty list.
        Assert.Contains("Loc.Get(\"ReleaseRecordEmpty\")", page, StringComparison.Ordinal);
        // The way back to the screen is an address, not a browser-history guess.
        Assert.Contains("ProjectRoutes.Release(Handle)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_page_links_to_the_release_that_named_it_and_says_nothing_when_there_is_none()
    {
        var inspector = Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor");

        Assert.Contains(
            "/cards/{Uri.EscapeDataString(Card.Reference.CardId)}/release",
            inspector,
            StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ReleaseCardLine\", release.Version)", inspector, StringComparison.Ordinal);
        Assert.Contains(
            "ProjectRoutes.ReleaseRecord(Board.Project.Handle, release.Version)",
            inspector,
            StringComparison.Ordinal);
        // The line is drawn only when a release answered: no release is no line, not an empty one.
        Assert.Contains("if (_release is { } release)", inspector, StringComparison.Ordinal);
        // A 404 is the answer "no release named this card" and is not treated as a failure.
        Assert.Contains("if (response.IsSuccessStatusCode)", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_addresses_are_written_once()
    {
        var routes = Read("src", "Aiko.Pwa", "Services", "ProjectRoutes.cs");

        Assert.Contains("public static string Release(string projectHandle)", routes, StringComparison.Ordinal);
        Assert.Contains(
            "public static string ReleaseRecord(string projectHandle, string version) =>",
            routes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_caption_the_release_markup_asks_for_exists_in_both_dictionaries()
    {
        var pages = new[]
        {
            Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor"),
            Read("src", "Aiko.Pwa", "Pages", "ReleaseRecordPage.razor"),
            Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor")
        };

        // The keys are taken from the markup rather than listed here: a caption typed into a page is exactly
        // the moment it has to exist in both dictionaries, and a list would need updating by hand.
        var keys = pages
            .SelectMany(page => Regex
                .Matches(page, "Loc\\.(?:Get|Format)\\(\"(?<key>[A-Za-z0-9_]+)\"")
                .Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(Neutral(key)),
                $"'{key}' is asked for by a release screen but missing from Loc.resx.");
            Assert.False(
                string.IsNullOrWhiteSpace(Russian(key)),
                $"'{key}' is asked for by a release screen but missing from Loc.ru.resx.");
        }
    }

    private static string Neutral(string key) =>
        Manager.GetString(key, CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Russian(string key) =>
        Manager.GetString(key, new CultureInfo("ru")) ?? string.Empty;

    /// <summary>Reads a file of this repository, walking up from this assembly to the solution.</summary>
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleaseHistoryScreenSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
