using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Aiko.Pwa.Resources;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the "how a release is conducted" section of the project's settings.
/// </summary>
/// <remarks>
/// The requirement that shapes the whole section is the pair of sentences about a release's side effects: what
/// it touches and what it leaves alone. Those are the words a person reads before installing an archive found
/// through this cockpit, and an answer that blurs them - or that quietly omits the second half - is read as
/// permission. So both halves are pinned here, together with the promise that the panel points at the release
/// screen rather than restating it.
/// </remarks>
public sealed class ReleaseProcedureSpecs
{
    private static readonly ResourceManager Manager = new("Aiko.Pwa.Resources.Loc", typeof(Loc).Assembly);

    [Fact]
    public void The_section_stands_among_the_projects_own_settings_panels()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ProjectSettingsPage.razor");
        var content = Between(page, "<ExtraNarrowContent>", "</ExtraNarrowContent>");

        // The panels of the narrow column: the agents this project is connected to, where the links registry
        // moved to, and how a release is conducted.
        Assert.Contains("Loc.Get(\"Agents\")", content, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"LinkedProjects\")", content, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseProcedureTitle\")", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_order_is_written_down_rather_than_left_to_the_workflow_files()
    {
        var order = Neutral("ReleaseProcedureOrder");

        // Tag, workflow, installation, agents: the four steps the card names, in that order.
        Assert.Contains("tag", order, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workflow", order, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install", order, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agents", order, StringComparison.OrdinalIgnoreCase);

        // And the panel says why it exists: so nobody reconstructs it from the pipeline files.
        Assert.Contains(
            "workflow files",
            Neutral("ReleaseProcedureHint"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_section_says_what_installing_touches_and_what_it_leaves_alone()
    {
        var touches = Neutral("ReleaseProcedureTouches");
        Assert.Contains(@"%LOCALAPPDATA%\Aiko\bin", touches, StringComparison.Ordinal);
        Assert.Contains("PATH", touches, StringComparison.Ordinal);

        // The half people actually ask about: their project's own data is not part of the installation.
        var keeps = Neutral("ReleaseProcedureKeeps");
        Assert.Contains(".aiko", keeps, StringComparison.Ordinal);
        Assert.Contains("aiko.db", keeps, StringComparison.Ordinal);
        Assert.Contains("token", keeps, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_points_at_the_release_screen_instead_of_repeating_it()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ProjectSettingsPage.razor");

        // The link is built by the one helper that speaks routes, and that helper follows ProjectRoutes - the
        // same section name the release page answers at and the rail links to.
        Assert.Contains("ReleaseHref(board.Project.Handle)", page, StringComparison.Ordinal);
        Assert.Contains("ProjectRoutes.Release(projectHandle)", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseProcedureOpen\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_procedure_caption_exists_in_both_dictionaries()
    {
        var neutral = Keys(Read("src", "Aiko.Pwa", "Resources", "Loc.resx"), "ReleaseProcedure");
        var russian = Keys(Read("src", "Aiko.Pwa", "Resources", "Loc.ru.resx"), "ReleaseProcedure");

        Assert.NotEmpty(neutral);
        Assert.Equal(
            neutral.OrderBy(key => key, StringComparer.Ordinal),
            russian.OrderBy(key => key, StringComparer.Ordinal));

        // A "translation" identical to the English is a key nobody wrote.
        Assert.All(neutral, key => Assert.NotEqual(Neutral(key), Russian(key)));
    }

    /// <summary>The part of a document between two markers, which is where a slot's content lives.</summary>
    private static string Between(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = text.IndexOf(close, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"'{open}' ... '{close}' should both be present.");
        return text[start..end];
    }

    /// <summary>The keys of one resource file whose name starts with a prefix.</summary>
    private static HashSet<string> Keys(string resx, string prefix) =>
        Regex
            .Matches(resx, $"<data name=\"(?<name>{prefix}[^\"]*)\"")
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
            Path.GetDirectoryName(typeof(ReleaseProcedureSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
