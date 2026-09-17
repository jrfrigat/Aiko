namespace Aiko.Pwa.Pages;

/// <summary>
/// One card's move between pipeline stages, as reported by the board.
/// </summary>
/// <param name="CardId">The card that moved.</param>
/// <param name="StageId">The stage it was dropped into.</param>
/// <remarks>
/// The board reports the move rather than the whole reordered list, so the page performs one
/// <c>PUT /stage</c> instead of diffing two projections to find what changed.
/// </remarks>
public sealed record BoardCardMove(string CardId, string StageId);

/// <summary>
/// The payload a board card carries while it is being dragged.
/// </summary>
/// <param name="CardId">The card's id.</param>
/// <param name="StageId">The stage the card was in when the drag started.</param>
/// <remarks>
/// Deliberately not the <c>Card</c> itself: the drag model only needs identity, and keeping the
/// domain record out of it means the board cannot accidentally treat a stale copy as live state.
/// </remarks>
internal sealed record BoardCardDrag(string CardId, string StageId);
