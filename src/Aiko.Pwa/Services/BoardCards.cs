using Aiko.Application.Contracts;
using Aiko.Domain.Cards;

namespace Aiko.Pwa.Services;

/// <summary>
/// Where a card lives: read from the board snapshot as one set rather than two.
/// </summary>
/// <remarks>
/// The snapshot carries the work and the history apart on purpose - <c>Cards</c> is what a column, a counter
/// and a priority may see, <c>ArchivedCards</c> is what was put away - so a screen that resolves one card by
/// its id has to ask both. Doing that by hand is what went wrong: the card page read <c>Cards</c> alone and
/// answered "no such card" for a card the board itself links to, while the board's panel read both. One place
/// answers the question now, so the two cannot drift apart again.
/// </remarks>
public static class BoardCards
{
    /// <summary>
    /// Every card the snapshot carries: the ones on the board first, then the archive, so a lookup keeps the
    /// board's own order where the two do not overlap.
    /// </summary>
    /// <param name="board">The board snapshot, or null while nothing is loaded.</param>
    /// <remarks>
    /// A snapshot from a daemon that says nothing about the archive carries a null <c>ArchivedCards</c>, which
    /// reads as an empty archive - the same reading the board's archive panel makes.
    /// </remarks>
    public static IEnumerable<Card> All(ProjectBoardSnapshot? board) =>
        (board?.Cards ?? []).Concat(board?.ArchivedCards ?? []);

    /// <summary>
    /// The card with this id, on the board or in the archive, or null when the snapshot carries neither.
    /// </summary>
    /// <param name="board">The board snapshot, or null while nothing is loaded.</param>
    /// <param name="cardId">The card id a command and a URL name the card by.</param>
    public static Card? Find(ProjectBoardSnapshot? board, string cardId) =>
        All(board).FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Reference.CardId, cardId));
}
