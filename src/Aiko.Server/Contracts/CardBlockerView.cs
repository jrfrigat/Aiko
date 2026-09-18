namespace Aiko.Server.Contracts;

/// <summary>
/// One card that blocks another one, as <c>aiko_get_card</c> reports it next to the card itself.
/// </summary>
/// <remarks>
/// A view rather than the domain's <see cref="Aiko.Domain.Workflow.CardBlocker"/>: this is the wire shape an
/// agent reads, and keeping it here means the domain record can change without silently changing the tool's
/// contract. The list is added beside the card's own fields instead of wrapping them, so a caller that reads
/// <c>title</c> or <c>stageId</c> keeps working after the blockers arrive.
/// </remarks>
/// <param name="CardId">Id of the blocking card.</param>
/// <param name="Title">Its title, for a message a reader can act on.</param>
/// <param name="StageId">Stage it sits in now.</param>
/// <param name="StageTitle">That stage's title.</param>
internal sealed record CardBlockerView(string CardId, string Title, string StageId, string StageTitle);
