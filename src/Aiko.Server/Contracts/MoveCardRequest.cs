namespace Aiko.Server.Contracts;

/// <summary>
/// Request to move a card to another board stage.
/// </summary>
internal sealed record MoveCardRequest(string StageId, long ExpectedRevision);
