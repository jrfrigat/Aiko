using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// Store of relations between cards; relations.json is the source of truth,
/// SQLite is the projection.
/// </summary>
public interface IRelationStore
{
    /// <summary>
    /// Returns all relations of the project.
    /// </summary>
    ValueTask<IReadOnlyList<CardRelation>> ListAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces a relation, verifying that the cards exist and no blocks cycle appears.
    /// </summary>
    ValueTask SaveAsync(CardRelation relation, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a relation by identifier; a missing relation is not an error.
    /// </summary>
    ValueTask RemoveAsync(
        string projectId,
        string relationId,
        CancellationToken cancellationToken);
}
