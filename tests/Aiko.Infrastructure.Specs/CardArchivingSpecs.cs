using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The board filter returns the stored list itself while nothing is archived - the ordinary case - and a
/// filtered list only when the archive is non-empty.
/// </summary>
public sealed class CardArchivingSpecs
{
    [Fact]
    public void A_board_without_an_archive_is_the_stored_list_itself()
    {
        var cards = MakeCards(200, archived: false);

        // Warm up, then the ordinary path must not allocate at all.
        for (var index = 0; index < 10; index++)
        {
            _ = CardArchiving.OnBoard(cards);
        }

        const int Calls = 1000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Calls; index++)
        {
            _ = CardArchiving.OnBoard(cards);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(cards, CardArchiving.OnBoard(cards));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Archived_cards_are_left_out_and_the_stored_list_is_untouched()
    {
        var open = MakeCard("OPEN", archived: false);
        var away = MakeCard("AWAY", archived: true);
        var stored = new Card[] { open, away };

        var board = CardArchiving.OnBoard(stored);

        Assert.Same(open, Assert.Single(board));
        Assert.Equal(2, stored.Length);
    }

    private static Card[] MakeCards(int count, bool archived) =>
        [.. Enumerable.Range(0, count).Select(index => MakeCard($"CARD-{index}", archived))];

    private static Card MakeCard(string cardId, bool archived) => new(
        new CardReference("project", cardId),
        "Task",
        cardId,
        "task",
        WorkflowDefinition.BacklogStageId,
        1,
        0m,
        [],
        [],
        archived
            ? new Dictionary<string, string> { [Card.ArchivedAtMetadataKey] = DateTimeOffset.UnixEpoch.ToString("O") }
            : new Dictionary<string, string>());
}
