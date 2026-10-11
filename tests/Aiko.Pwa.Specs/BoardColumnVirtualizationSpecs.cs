using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the board column's body: the cards are drawn through <c>Virtualize</c>, so only the window a reader
/// can see is in the DOM - a column of hundreds of cards no longer lays all of them out.
/// </summary>
/// <remarks>
/// The card's own mark-up must survive the move into the virtualized list unchanged, and everything that is
/// not the card list - the column's banner, its WIP counter, the drop target and its caption - stays outside
/// it. The page is read as text, the way the specs beside this one read it.
/// </remarks>
public sealed class BoardColumnVirtualizationSpecs
{
    [Fact]
    public void The_column_body_draws_its_cards_through_the_virtualizer()
    {
        var board = Read("src", "Aiko.Pwa", "Pages", "BoardSection.razor");

        // The list is virtualized, and the item is named so the card's mark-up can ask for it...
        Assert.Contains("<Virtualize", board, StringComparison.Ordinal);
        Assert.Contains("Items=\"stageCards\"", board, StringComparison.Ordinal);
        Assert.Contains("Context=\"card\"", board, StringComparison.Ordinal);
        // ...with an item size and an overscan, so a variable-height card does not leave a visible gap.
        Assert.Contains("ItemSize=", board, StringComparison.Ordinal);
        Assert.Contains("OverscanCount=", board, StringComparison.Ordinal);
        // The framework's virtualizer lives in its own namespace, which the board brings in itself.
        Assert.Contains(
            "@using Microsoft.AspNetCore.Components.Web.Virtualization",
            board,
            StringComparison.Ordinal);

        // The card's own mark-up is the one the board always drew: the drag payload and the id's link.
        Assert.Contains(
            "new BoardCardDrag(card.Reference.CardId, card.StageId)",
            board,
            StringComparison.Ordinal);
        Assert.Contains("ProjectRoutes.Card(Board.Project.Handle, card.Reference.CardId)", board, StringComparison.Ordinal);

        // What is not the card list stays outside the virtualizer: the column's counter, the drop caption
        // and the drop zone the cards are dropped into.
        Assert.Contains("WIP: @stageCards.Length", board, StringComparison.Ordinal);
        Assert.Contains("Class=\"aiko-col__drop\"", board, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"DropHere\")", board, StringComparison.Ordinal);
    }

    /// <summary>Reads a file of this repository, walking up from this assembly to the solution.</summary>
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BoardColumnVirtualizationSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
