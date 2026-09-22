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
        // What the screen reads is addressed by the handle it was opened with: the project's releases, and the
        // settings document that holds the schemes a release can follow.
        Assert.Contains("api/v1/projects/{Uri.EscapeDataString(Handle)}/releases", page, StringComparison.Ordinal);
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
    public void The_facts_the_screen_used_to_probe_are_gone()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // The block is gone rather than hidden: the screen no longer reads the daemon's probe of the
        // repository, and nothing of it is left to draw. A tile that said "unknown" with nothing behind it
        // would be worse than the tile it replaced.
        Assert.DoesNotContain("ReleaseInfo", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseFactsTitle", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseUnknown", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseOrderTitle", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_screen_offers_the_policy_and_the_agent_and_places_one_command()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // Creating a release is one dialog: which policy it follows, and which agent conducts it. The schemes
        // are the project's own - read from the settings document rather than invented by the screen - and the
        // agents are the ones the cockpit already knows about.
        Assert.Contains("FlareDialog", page, StringComparison.Ordinal);
        Assert.Contains("api/v1/projects/{Uri.EscapeDataString(Handle)}/settings", page, StringComparison.Ordinal);
        Assert.Contains("ReleasePolicyLabel", page, StringComparison.Ordinal);
        Assert.Contains("foreach (var agent in State.Agents)", page, StringComparison.Ordinal);

        // And it is one command: the policy rides in the text, the agent in the field the queue already has
        // for it, and a release that names no agent is one any agent may take.
        Assert.Contains("CardCommandAction.Release", page, StringComparison.Ordinal);
        Assert.Contains("CardId: null,", page, StringComparison.Ordinal);
        Assert.Contains("Text: _policyId", page, StringComparison.Ordinal);
        Assert.Contains(
            "AgentAdapterId: string.IsNullOrWhiteSpace(_agentId) ? null : _agentId",
            page,
            StringComparison.Ordinal);

        // Waiting is read from the queue rather than remembered by the page.
        Assert.Contains("State.Commands.FirstOrDefault(command =>", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ReleaseCommandWaiting\", waiting.Id)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_screen_denies_the_push_and_the_installation_in_words()
    {
        // Aiko runs no agent, pushes no tag and installs nothing. The denial lives in the dialog, where a
        // person reads before confirming: a screen that dropped those words would promise work nobody does.
        var hint = Neutral("ReleaseCreateHint");
        Assert.Contains("does not run agents", hint, StringComparison.Ordinal);
        Assert.Contains("does not push the tag", hint, StringComparison.Ordinal);
        Assert.Contains("installs nothing", hint, StringComparison.Ordinal);

        // The button names what it opens, and the confirmation names what it places - a request in the queue,
        // not a release.
        Assert.Contains("queue", Neutral("ReleaseCreateConfirm"), StringComparison.Ordinal);
        Assert.Contains(
            "Loc.Get(\"ReleaseCreateButton\")",
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
