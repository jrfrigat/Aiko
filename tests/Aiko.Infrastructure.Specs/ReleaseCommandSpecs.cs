using Aiko.Domain.Execution;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The release command: the second action that is about the project rather than about a card.
/// </summary>
/// <remarks>
/// A release is placed from the release screen, and what it must not become is a command about a card - so
/// the two rules that keep the board pass usable are pinned here for it as well: it names no card, and only
/// one of them is open at a time. Two releases at once would be two tags for one tree.
/// </remarks>
public sealed class ReleaseCommandSpecs
{
    [Fact]
    public void A_release_names_no_card_and_needs_none()
    {
        // The version the person has in mind rides in the text, so an empty text is still a valid request:
        // the procedure chooses the version and explains the choice.
        CardCommands.Validate(CardCommandAction.Release, cardId: null, stageId: null, executionId: null, text: null);
        CardCommands.Validate(CardCommandAction.Release, cardId: null, stageId: null, executionId: null, text: "v0.2.0");

        // A release that named a card would show up on that card's screen and nowhere else.
        Assert.Throws<ArgumentException>(
            () => CardCommands.Validate(CardCommandAction.Release, "TASK-1", null, null, null));
    }

    [Fact]
    public void A_second_release_is_refused_while_one_is_open()
    {
        var refused = CardCommands.RefusePlace(CardCommandAction.Release, [Command(CardCommandState.Queued)]);
        Assert.NotNull(refused);
        Assert.Contains("already under way", refused, StringComparison.Ordinal);

        // A closed release is history: the next one is a new command, not a refusal.
        Assert.Null(CardCommands.RefusePlace(
            CardCommandAction.Release,
            [Command(CardCommandState.Completed)]));
    }

    private static CardCommand Command(CardCommandState state) => new(
        Id: "CMD-1",
        CardId: null,
        Action: CardCommandAction.Release,
        StageId: null,
        ExecutionId: null,
        AgentAdapterId: null,
        Text: null,
        State: state,
        RequestedBy: "a person",
        CreatedAtUtc: DateTimeOffset.UnixEpoch,
        ClaimedAtUtc: null,
        FinishedAtUtc: null,
        Message: null);
}
