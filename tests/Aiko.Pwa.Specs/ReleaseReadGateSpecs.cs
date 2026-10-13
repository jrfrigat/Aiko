using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// The release screens stated things about a project they had not read: the plan panel said "no plans" and the
/// history panel "nothing recorded" while the three reads behind them were still in flight, and the record
/// page's tiles said "missing" and "0" about a record that had not arrived. An empty answer and an unanswered
/// read are different facts, and a screen owes one answer per read.
/// </summary>
/// <remarks>
/// Read from the markup, as the other screen specs do, until the PWA has component tests (TASK-216). What each
/// gate is compared against is a key rather than a flag, because a switch leaves the previous project's plan
/// and the previous release's date in the very fields the panels draw.
/// </remarks>
public sealed class ReleaseReadGateSpecs
{
    [Fact]
    public void The_plan_and_the_history_are_not_claimed_before_the_read_that_speaks_for_them()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // The gate is the project the values were read for: a flag would be set already on a project switch,
        // when the panels are holding the project that was open before.
        Assert.Contains("private bool Waiting => _loadedFor != Handle;", page, StringComparison.Ordinal);

        // Both panels draw the wait in their own shape - rows of versions, rows of releases - and say nothing
        // about the project until then.
        var waits = Regex.Matches(page, "<AikoSkeletonRegion>");
        Assert.Equal(2, waits.Count);
        var plan = page.IndexOf("Loc.Get(\"ReleasePlanEmpty\")", StringComparison.Ordinal);
        var history = page.IndexOf("Loc.Get(\"ReleaseHistoryEmpty\")", StringComparison.Ordinal);
        Assert.True(plan > waits[0].Index, "The plan panel should answer the wait before it says there are no plans.");
        Assert.True(history > waits[1].Index, "The history panel should answer the wait before it says nothing is recorded.");

        // And both claims belong to the answer that arrived, which is what the else-if chain says.
        Assert.Contains("@if (Waiting)", page, StringComparison.Ordinal);
        Assert.Contains("else if (PlannedVersions.Count == 0)", page, StringComparison.Ordinal);
        Assert.Contains("else if (_history.Count > 0)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wait_ends_on_every_answer_including_a_failure()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");
        var reload = Method(page, "private async Task ReloadAsync()");

        // Three endings and no more: the project that did not open, the read that landed and the read that
        // failed. A skeleton under a reason that has already been given would say the screen is still finding
        // out, and a screen that never stops waiting hides the values it did read.
        Assert.Equal(3, Regex.Matches(reload, "_loadedFor = Handle;").Count);
    }

    [Fact]
    public void The_records_figures_are_not_drawn_before_the_record_arrives()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleaseRecordPage.razor");

        // The record is per release, not per project: the key is both, because the page survives a switch from
        // one release to another as well as one from one project to another.
        Assert.Contains("private bool Waiting => _loadedFor != ReadKey;", page, StringComparison.Ordinal);
        Assert.Contains("private string ReadKey => $\"{Handle}/{Version}\";", page, StringComparison.Ordinal);

        // The wait is drawn in the panel's own shape, and the figures - "missing", "0", the date that is not
        // there - come after it.
        var skeleton = page.IndexOf("<AikoSkeletonRegion>", StringComparison.Ordinal);
        Assert.True(skeleton >= 0, "The record page should draw its own shape while the record is being read.");
        Assert.True(
            skeleton < page.IndexOf("Loc.Get(\"ReleaseRecordMissing\")", StringComparison.Ordinal),
            "The scheme tile should not say a record is missing before the read has answered.");
        Assert.True(
            skeleton < page.IndexOf("@RecordedAt", StringComparison.Ordinal),
            "The date tile should not be drawn before the read has answered.");

        // And the wait ends on the answer, wherever it came from: the refusal that returns early is one ending,
        // the finally is the other two, and there is no path that waits forever.
        var reload = Method(page, "private async Task ReloadAsync()");
        Assert.Equal(2, Regex.Matches(reload, "_loadedFor = ReadKey;").Count);
    }

    [Fact]
    public void The_words_for_an_empty_answer_are_kept_for_the_answer_that_arrived()
    {
        // Deleting the words would trade one wrong answer for another: a project with no plans, a project that
        // has recorded nothing and a release that carried nothing are all real, read answers.
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");
        Assert.Contains("Loc.Get(\"ReleasePlanEmpty\")", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseHistoryEmpty\")", page, StringComparison.Ordinal);

        var record = Read("src", "Aiko.Pwa", "Pages", "ReleaseRecordPage.razor");
        Assert.Contains("Loc.Get(\"ReleaseRecordMissing\")", record, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleaseRecordEmpty\")", record, StringComparison.Ordinal);
        // The empty-release branch is still guarded by the record itself, so it is an answer and not a wait.
        Assert.Contains("else if (_record is not null)", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body of one method of a page, from its signature to its closing brace - which is the first line that
    /// closes at the method's own indentation.
    /// </summary>
    private static string Method(string page, string signature)
    {
        var start = page.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} should be there.");
        var body = page[start..];
        return body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleaseReadGateSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
