using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the board column's render path: the per-card facts a column draws are read from the snapshot once
/// per render and then answered by id, instead of scanning the snapshot's collections for every badge.
/// </summary>
/// <remarks>
/// A column that answers "what is this card's score/state/parent/children" by scanning the snapshot pays
/// O(N) per question, for every question, on every render - O(N²) for a board of N cards, which is what made
/// the board lag with many cards. The page is read as text, the way the specs beside this one read it,
/// because the regression is a shape of the code rather than a value it returns.
/// </remarks>
public sealed class BoardRenderIndexSpecs
{
    [Fact]
    public void The_column_builds_its_per_card_facts_once_and_then_answers_by_id()
    {
        var board = Read("src", "Aiko.Pwa", "Pages", "BoardSection.razor");

        // The facts are gathered in one place, once for the whole render...
        Assert.Contains("private void BuildIndexes()", board, StringComparison.Ordinal);
        Assert.Contains("BuildIndexes();", board, StringComparison.Ordinal);
        // ...reading board and archive through the one helper that knows both.
        Assert.Contains("BoardCards.All(Board)", board, StringComparison.Ordinal);

        // ...and each per-card question is answered from the index rather than by a fresh scan.
        Assert.Contains(
            "_priorityByCard.TryGetValue(card.Reference.CardId, out var priority)",
            board,
            StringComparison.Ordinal);
        Assert.Contains(
            "_stateByCard.TryGetValue(card.Reference.CardId, out var state)",
            board,
            StringComparison.Ordinal);
        Assert.Contains(
            "_parentIdByCard.TryGetValue(card.Reference.CardId, out var parentId)",
            board,
            StringComparison.Ordinal);
        Assert.Contains(
            "_childIdsByCard.TryGetValue(card.Reference.CardId, out var children)",
            board,
            StringComparison.Ordinal);

        // The one place a card is resolved by its id is still the shared helper, which the index falls back
        // to - a screen that scanned its own `Cards` for that is exactly the defect the archive spec forbids.
        Assert.Contains("BoardCards.Find(Board, cardId)", board, StringComparison.Ordinal);
        Assert.DoesNotContain("Cards.FirstOrDefault(candidate =>", board, StringComparison.Ordinal);
    }

    /// <summary>Reads a file of this repository, walking up from this assembly to the solution.</summary>
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BoardRenderIndexSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
