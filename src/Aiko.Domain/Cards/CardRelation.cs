namespace Aiko.Domain.Cards;

/// <summary>
/// Typed directed relation between two cards of the same project.
/// </summary>
public sealed record CardRelation
{
    /// <summary>
    /// Creates a relation. Cross-project relations and self-references are rejected.
    /// </summary>
    public CardRelation(string id, CardReference source, CardReference target, string type, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        if (!StringComparer.Ordinal.Equals(source.ProjectId, target.ProjectId))
        {
            throw new ArgumentException("Cross-project relations are not supported in the MVP.");
        }

        if (source == target)
        {
            throw new ArgumentException("A card cannot relate to itself.");
        }

        Id = id;
        Source = source;
        Target = target;
        Type = type;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Relation identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Source card of the relation.
    /// </summary>
    public CardReference Source { get; }

    /// <summary>
    /// Target card of the relation.
    /// </summary>
    public CardReference Target { get; }

    /// <summary>
    /// Relation type; see <see cref="RelationTypes"/>.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// UTC timestamp of when the relation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Whether the relation is symmetric: true only for <see cref="RelationTypes.RelatesTo"/>.
    /// </summary>
    public bool IsSymmetric => StringComparer.Ordinal.Equals(Type, RelationTypes.RelatesTo);
}
