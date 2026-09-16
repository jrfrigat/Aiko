namespace Aiko.Domain.Cards;

/// <summary>
/// Unique reference to a card within a specific project.
/// </summary>
public sealed record CardReference
{
    /// <summary>
    /// Creates a reference to card <paramref name="cardId"/> in project <paramref name="projectId"/>.
    /// </summary>
    public CardReference(string projectId, string cardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ProjectId = projectId;
        CardId = cardId;
    }

    /// <summary>
    /// Identifier of the project that owns the card.
    /// </summary>
    public string ProjectId { get; }

    /// <summary>
    /// File-system-safe card identifier, for example <c>TASK-001</c>.
    /// </summary>
    public string CardId { get; }
}
