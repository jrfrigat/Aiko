using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the release plan on the release screen: what the page reads, what it draws, and that reading is all
/// it does.
/// </summary>
/// <remarks>
/// The page is read as text, the way the specs beside this one read it: the parts worth pinning are the
/// address, the captions and the conditions - the things a refactor moves without a compiler noticing. The
/// caption check is not repeated here; <see cref="ReleaseHistoryScreenSpecs"/> already derives the keys from
/// this page and fails on one missing from either dictionary.
/// </remarks>
public sealed class ReleasePlanScreenSpecs
{
    [Fact]
    public void The_release_screen_reads_the_plan_by_the_handle_it_was_opened_with()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        Assert.Contains(
            "api/v1/projects/{Uri.EscapeDataString(Handle)}/release-plan",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "Http.GetFromJsonAsync<IReadOnlyList<ReleasePlanView>>(",
            page,
            StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ReleasePlanTitle\")", page, StringComparison.Ordinal);
        // An empty plan is said in words rather than left as a gap.
        Assert.Contains("Loc.Get(\"ReleasePlanEmpty\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_panel_says_how_ready_the_version_is_and_names_what_holds_a_card()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        // Readiness is the daemon's answer, said in words rather than left to be inferred from the counts
        // beside it - it is the one thing a person looks for before releasing.
        Assert.Contains(
            "Loc.Get(plan.IsReleasable ? \"ReleasePlanReleasable\" : \"ReleasePlanNotReleasable\")",
            page,
            StringComparison.Ordinal);
        // The counts travel from the daemon rather than being counted here, and the current version is marked.
        Assert.Contains("Loc.Format(\"ReleasePlanCounts\"", page, StringComparison.Ordinal);
        Assert.Contains("if (plan.IsCurrent)", page, StringComparison.Ordinal);

        // Every state a card can be in has words, and "blocked" is never left without "by what".
        foreach (var key in new[]
                 {
                     "ReleasePlanCardFinished",
                     "ReleasePlanCardInWork",
                     "ReleasePlanCardBlocked",
                     "ReleasePlanCardMissing"
                 })
        {
            Assert.Contains($"\"{key}\"", page, StringComparison.Ordinal);
        }

        Assert.Contains("card.BlockedBy.Count > 0", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ReleasePlanBlockedBy\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_planned_card_is_opened_through_the_one_helper_that_builds_card_addresses()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "ReleasePage.razor");

        Assert.Contains("ProjectRoutes.Card(Handle, card.CardId)", page, StringComparison.Ordinal);
        // A card the project no longer has is drawn by its id with a word instead of an empty place.
        Assert.Contains("card.Title ?? Loc.Get(\"ReleasePlanCardMissingTitle\")", page, StringComparison.Ordinal);
    }

    /// <summary>Reads a file of this repository, walking up from this assembly to the solution.</summary>
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleasePlanScreenSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
