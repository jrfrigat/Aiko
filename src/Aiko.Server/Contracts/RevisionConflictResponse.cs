namespace Aiko.Server.Contracts;

/// <summary>
/// Revision conflict response for optimistic updates.
/// </summary>
internal sealed record RevisionConflictResponse(long ExpectedRevision, long ActualRevision);
