using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// Card store: card.json files as the source of truth and SQLite as the read projection.
/// </summary>
public interface ICardStore
{
    /// <summary>
    /// Returns all cards of the project from the projection.
    /// </summary>
    ValueTask<IReadOnlyList<Card>> ListAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a card from its card.json file or returns null.
    /// </summary>
    ValueTask<Card?> FindAsync(CardReference reference, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a card with an optimistic revision check
    /// (<paramref name="expectedRevision"/> + 1) and updates the projection.
    /// </summary>
    ValueTask SaveAsync(Card card, long expectedRevision, CancellationToken cancellationToken);
}
