using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Aiko.Pwa.Resources;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the release screen: where it answers, what it draws, and what it refuses to promise.
/// </summary>
/// <remarks>
/// Two of these are the kind of thing no build catches. The first is the route: the page and the rail link
/// to the same address, and a section name typed in two places drifts. The second is the promise: this screen
/// stands next to a procedure that ends in a tag and a published release, and it would be easy for its words
/// to claim the push and the installation are done for the person. They are not - Aiko runs no agent - so the
/// denial is pinned here rather than trusted to whoever edits the captions next.
/// </remarks>
public sealed class ReleasePageSpecs
{
    private static readonly ResourceManager Manager = new("Aiko.Pwa.Resources.Loc", typeof(Loc).Assembly);

    [Fact]
    public void The_release_page_answers_at_the_projects_own_address()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        Assert.Contains("@page \"/p/{Handle}/release\"", page, StringComparison.Ordinal);
        // The facts come from the route TASK-127 added, addressed by the handle the page was opened with.
        Assert.Contains("api/v1/projects/{Uri.EscapeDataString(Handle)}/release", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_name_is_written_once_and_the_rail_links_to_it()
    {
        var routes = Read("src", "Aiko.Pwa", "Services", "ProjectRoutes.cs");
        var layout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");

        // The screen's name is one decision, taken in ProjectRoutes - the helper the page and the rail both
        // follow.
        Assert.Contains(
            "public static string Release(string projectHandle) => Section(projectHandle, \"release\");",
            routes,
            StringComparison.Ordinal);

        // And the rail carries it among the project's own pages.
        Assert.Contains("SectionHref(open.Handle, \"release\")", layout, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseNav\")", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_fact_is_drawn_as_unknown_and_never_as_an_empty_place()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // Both versions, the last release and the tree state fall back to the same word ...
        Assert.Equal(4, Regex.Matches(page, @"Loc\.Get\(""ReleaseUnknown""\)").Count);

        // ... and each fact the daemon could not read carries its reason, so "unknown" and "broken" do not
        // look alike on the screen.
        Assert.Contains("LatestReleaseReason", page, StringComparison.Ordinal);
        Assert.Contains("TreeStateReason", page, StringComparison.Ordinal);
        Assert.Contains(
            "_facts?.Latest is { Known: false, Failure: { Length: > 0 } failure }",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "_facts?.Head is { Known: false, Failure: { Length: > 0 } failure }",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_screen_reads_both_versions_from_the_daemon_it_is_talking_to()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        Assert.Contains("State.System?.Version", page, StringComparison.Ordinal);
        Assert.Contains("State.System?.AssetsVersion", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_a_release_puts_a_command_in_the_queue_and_shows_that_it_waits()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // The command is the one the queue already carries, and it names no card: a release is the project's.
        Assert.Contains("CardCommandAction.Release", page, StringComparison.Ordinal);
        Assert.Contains("new PlaceCommandRequest(", page, StringComparison.Ordinal);
        Assert.Contains("CardId: null,", page, StringComparison.Ordinal);

        // The version the person typed rides in the text; an empty field leaves the choice to the procedure,
        // which is what the command's own documentation says Text is for.
        Assert.Contains("Text: string.IsNullOrWhiteSpace(_version)", page, StringComparison.Ordinal);

        // Waiting is read from the queue rather than remembered by the page.
        Assert.Contains("State.Commands.FirstOrDefault(command =>", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ReleaseCommandWaiting\", waiting.Id)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_screen_denies_the_push_and_the_installation_in_words()
    {
        // Aiko runs no agent and installs nothing. The screen says both, where a person reads before pressing
        // the button and before following the steps.
        Assert.Contains("does not run agents", Neutral("ReleaseCommandHint"), StringComparison.Ordinal);

        var orderHint = Neutral("ReleaseOrderHint");
        Assert.Contains("you run yourself", orderHint, StringComparison.Ordinal);
        Assert.Contains("installs nothing", orderHint, StringComparison.Ordinal);

        // The button names what it does - it queues a request - rather than what would follow from it.
        Assert.Contains("queue", Neutral("ReleaseCommandButton"), StringComparison.Ordinal);

        // And the page still points at the order the whole procedure follows, so nobody goes looking.
        Assert.Contains(
            "Loc.Get(\"ReleaseSettingsLink\")",
            Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_release_caption_exists_in_both_dictionaries()
    {
        var neutral = ReleaseKeys(Read("src", "Aiko.Pwa", "Resources", "Loc.resx"));
        var russian = ReleaseKeys(Read("src", "Aiko.Pwa", "Resources", "Loc.ru.resx"));

        // An empty set would mean this spec is checking nothing at all.
        Assert.NotEmpty(neutral);
        Assert.Equal(
            neutral.OrderBy(key => key, StringComparer.Ordinal),
            russian.OrderBy(key => key, StringComparer.Ordinal));

        // A translated caption identical to the English one is a key nobody wrote; the placeholder is the one
        // value both dictionaries may share.
        Assert.All(
            neutral.Where(key => !key.EndsWith("Placeholder", StringComparison.Ordinal)),
            key => Assert.NotEqual(Neutral(key), Russian(key)));
    }

    /// <summary>The release keys declared in one resource file.</summary>
    private static HashSet<string> ReleaseKeys(string resx) =>
        Regex
            .Matches(resx, "<data name=\"(?<name>Release[^\"]*)\"")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Neutral(string key) =>
        Manager.GetString(key, CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"'{key}' is missing from the neutral resources.");

    private static string Russian(string key) =>
        Manager.GetString(key, new CultureInfo("ru"))
        ?? throw new InvalidOperationException($"'{key}' is missing from the Russian resources.");

    /// <summary>Reads a file of this repository, walking up from this assembly to the solution.</summary>
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleasePageSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
