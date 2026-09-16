namespace Aiko.Application.Contracts;

/// <summary>
/// Revision conflict during an optimistic save of an entity (card, workflow).
/// </summary>
public sealed class RevisionConflictException(
    string entity,
    long expectedRevision,
    long actualRevision)
    : InvalidOperationException(
        $"Revision conflict for {entity}: expected {expectedRevision}, actual {actualRevision}.")
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
