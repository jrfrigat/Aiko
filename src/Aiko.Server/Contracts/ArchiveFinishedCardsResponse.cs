namespace Aiko.Server.Contracts;

/// <summary>
/// What putting the finished cards away came to: the cards that went, and the ones the archive gate refused.
/// </summary>
/// <remarks>
/// A mass action that answered with a bare <c>204</c> would be a screen that claims more than it did. The two
/// lists are what lets the button say what happened: a card can be standing in the done stage and still not be
/// archivable - its closing run left open, its revision changed while the request was being served - and that is
/// worth naming rather than swallowing.
/// <para>
/// Only cards the action <em>considered</em> appear here. A card that has not finished its pipeline is not a
/// refusal: the request is "archive the finished ones", and answering it with a list of the whole board would be
/// an answer to a question nobody asked.
/// </para>
/// </remarks>
/// <param name="Archived">Cards put into the archive, by id.</param>
/// <param name="Refused">Cards the gate would not accept, each with the reason it gave.</param>
internal sealed record ArchiveFinishedCardsResponse(
    IReadOnlyList<string> Archived,
    IReadOnlyList<ArchiveRefusal> Refused);

/// <summary>
/// One card the archive gate would not accept, and why - the domain's own words, passed through unchanged.
/// </summary>
/// <param name="CardId">Card that stayed on the board.</param>
/// <param name="Reason">What the gate said.</param>
internal sealed record ArchiveRefusal(string CardId, string Reason);
