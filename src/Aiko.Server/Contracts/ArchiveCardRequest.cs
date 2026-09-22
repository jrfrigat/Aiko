namespace Aiko.Server.Contracts;

/// <summary>
/// Request to put a card into the archive or to return it to the board.
/// </summary>
/// <remarks>
/// One request for both directions, because they are one action on one field: <c>archived</c> says which way,
/// and the revision says which card the caller was looking at. Two routes would be two places to keep the same
/// revision check and the same refusal in step.
/// </remarks>
/// <param name="Archived">True to put the card away, false to bring it back.</param>
/// <param name="ExpectedRevision">Revision the caller read the card at.</param>
internal sealed record ArchiveCardRequest(bool Archived, long ExpectedRevision);
