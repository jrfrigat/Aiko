using Aiko.Application.Contracts;
using Aiko.Domain.Cards;

namespace Aiko.Application.Cards;

/// <summary>
/// Invents the identifier a new card gets.
/// </summary>
/// <remarks>
/// An id is Aiko's own bookkeeping - it names the card's folder inside <c>.aiko</c> - so asking a person for
/// one asked them to do the computer's job and gave them a way to collide with a card that already exists.
/// The shape stays the one the project already reads: the type's id in capitals and the lowest free number,
/// so a task is <c>TASK-1</c>, a story <c>STORY-1</c>, and a project that adds an <c>Epic</c> type numbers its
/// own cards from <c>EPIC-1</c> without anyone deciding anything.
/// </remarks>
public static class CardIdGenerator
{
    /// <summary>
    /// Returns the next free id for a card type in a project.
    /// </summary>
    /// <param name="cards">The project's cards, which is what "free" is read against.</param>
    /// <param name="projectId">Project the card will belong to.</param>
    /// <param name="kind">Card type id, for example <c>Task</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask<string> NextAsync(
        ICardStore cards,
        string projectId,
        string kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var prefix = CardKind.ToWorkflowId(kind).ToUpperInvariant();
        var taken = (await cards.ListAsync(projectId, cancellationToken))
            .Select(card => card.Reference.CardId)
            .ToHashSet(StringComparer.Ordinal);

        // The first free number, not "the highest plus one": a deleted card leaves no hole to remember, and a
        // card whose id was typed by hand cannot push the sequence somewhere odd.
        for (var number = 1; ; number++)
        {
            var candidate = $"{prefix}-{number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
