namespace Aiko.Application.Contracts;

/// <summary>
/// Revision conflict during an optimistic save of an entity (card, workflow).
/// </summary>
public sealed class RevisionConflictException(
    string entity,
    long expectedRevision,
    long actualRevision)
    : InvalidOperationException(
        $"Revision conflict for {entity}: expected {expectedRevision}, actual {actualRevision}. It changed after "
        + $"it was read - a start, a completion or another writer moves the revision. Read it again (aiko_get_card "
        + $"for a card) and retry with revision {actualRevision}.")
{
    /// <summary>
    /// Revision expected by the caller.
    /// </summary>
    public long ExpectedRevision { get; } = expectedRevision;

    /// <summary>
    /// Actual revision of the entity in the store.
    /// </summary>
    public long ActualRevision { get; } = actualRevision;
}
