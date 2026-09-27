using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards where a card is read from. The board snapshot keeps the work and the archive apart on purpose - a
/// column, a counter and a priority must not see a card that was put away - so a screen that resolves one card
/// by its id has to ask both sets. Reading the board alone is what made an archived card answer "no such card"
/// on a page the board's own archive panel links to.
/// </summary>
public sealed class ArchivedCardVisibilitySpecs
{
    [Fact]
    public void A_card_that_was_put_away_is_still_found_by_its_id()
    {
        var board = Board(
            cards: [CardOnBoard("TASK-1")],
            archived: [CardInArchive("STORY-15")]);

        var found = BoardCards.Find(board, "STORY-15");

        Assert.NotNull(found);
        Assert.True(found!.IsArchived);
        Assert.Equal("STORY-15", found.Reference.CardId);
    }

    [Fact]
    public void A_card_on_the_board_is_found_as_before()
    {
        var board = Board(
            cards: [CardOnBoard("TASK-1")],
            archived: [CardInArchive("STORY-15")]);

        var found = BoardCards.Find(board, "TASK-1");

        Assert.NotNull(found);
        Assert.False(found!.IsArchived);
    }

    [Fact]
    public void Both_sets_are_read_together_with_the_board_first()
    {
        var board = Board(
            cards: [CardOnBoard("TASK-1")],
            archived: [CardInArchive("STORY-15")]);

        // The order is what a lookup keeps, and what a screen listing them lists: the work the board draws,
        // then the history it does not.
        Assert.Equal(["TASK-1", "STORY-15"], BoardCards.All(board).Select(card => card.Reference.CardId));
    }

    [Fact]
    public void An_older_daemon_that_says_nothing_about_the_archive_reads_as_an_empty_one()
    {
        var board = Board(cards: [CardOnBoard("TASK-1")], archived: null);

        Assert.Equal(["TASK-1"], BoardCards.All(board).Select(card => card.Reference.CardId));
        Assert.Null(BoardCards.Find(board, "STORY-15"));
    }

    [Fact]
    public void A_card_no_set_carries_is_not_found()
    {
        var board = Board(cards: [CardOnBoard("TASK-1")], archived: [CardInArchive("STORY-15")]);

        // Nothing loaded, and a card the snapshot never carried: both are "no such card", which is what the
        // card page still has to be able to say.
        Assert.Null(BoardCards.Find(null, "TASK-1"));
        Assert.Null(BoardCards.Find(board, "TASK-404"));
        Assert.Empty(BoardCards.All(null));
    }

    [Fact]
    public void The_screens_that_resolve_a_card_by_its_id_ask_the_one_place_that_knows()
    {
        var cardPage = Read("src", "Aiko.Pwa", "Pages", "CardPage.razor");
        var mainLayout = Read("src", "Aiko.Pwa", "Layout", "MainLayout.razor");
        var boardSection = Read("src", "Aiko.Pwa", "Pages", "BoardSection.razor");

        // The card page opens a card the board's archive panel links to, so it reads both sets ...
        Assert.Contains("BoardCards.Find(State.Board, CardId)", cardPage, StringComparison.Ordinal);
        // ... and says why the card is not on the board instead of leaving the reader to guess.
        Assert.Contains("private bool IsArchived => Card?.IsArchived", cardPage, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"CardArchivedNotice\")", cardPage, StringComparison.Ordinal);

        // The quick search answers "where did that card go?", so it searches the archive as well, and the row
        // marks a match that is not on the board.
        Assert.Contains("BoardCards.All(board)", mainLayout, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ArchiveBadge\")", mainLayout, StringComparison.Ordinal);

        // The board's own panel asks the same place rather than keeping a second reading of its own.
        Assert.Contains("BoardCards.Find(Board, cardId)", boardSection, StringComparison.Ordinal);

        // A screen that resolves one card by its own `Cards` lookup is exactly the defect this card is about.
        Assert.DoesNotContain("Cards.FirstOrDefault(card =>", cardPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Cards.FirstOrDefault(candidate =>", boardSection, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_interface_texts_are_carried_by_both_languages()
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var text = Read("src", "Aiko.Pwa", "Resources", file);

            Assert.Contains("name=\"CardArchivedNotice\"", text, StringComparison.Ordinal);
            Assert.Contains("name=\"ArchiveBadge\"", text, StringComparison.Ordinal);
        }
    }

    private static Card CardOnBoard(string cardId) =>
        CardAt(cardId, archived: false);

    private static Card CardInArchive(string cardId) =>
        CardAt(cardId, archived: true);

    private static Card CardAt(string cardId, bool archived)
    {
        // The archive mark is metadata, not a stage: being out of the way is a reading of a card rather than
        // somewhere it was moved to, which is why a card in the archive still has a stage of its own.
        var metadata = archived
            ? Card.WithArchivedAt(
                new Dictionary<string, string>(StringComparer.Ordinal),
                DateTimeOffset.UnixEpoch)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return new Card(
            new CardReference("project", cardId),
            "Task",
            $"Title of {cardId}",
            "task",
            "done",
            1,
            0m,
            [],
            [],
            metadata);
    }

    private static ProjectBoardSnapshot Board(IReadOnlyList<Card> cards, IReadOnlyList<Card>? archived) =>
        new(
            new RegisteredProject("project", "Project", @"C:\project", "project"),
            [],
            [],
            cards,
            [],
            [],
            null,
            archived);

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. path]));

    /// <summary>Walks up from this assembly to the solution file, the way the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ArchivedCardVisibilitySpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
