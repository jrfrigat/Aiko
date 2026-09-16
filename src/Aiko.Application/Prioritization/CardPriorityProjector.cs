using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Prioritization;

/// <summary>
/// Projects effective card priorities from cards and their parent-child relations
/// using <see cref="PriorityCalculator"/>. In a parent-child relation the source card
/// is treated as the parent.
/// </summary>
public static class CardPriorityProjector
{
    /// <summary>
    /// Computes a <see cref="CardPriority"/> for every card: cards without parents keep
    /// their own priority, others blend it with the maximum parent value.
    /// </summary>
    /// <param name="cards">All cards of the board.</param>
    /// <param name="relations">All relations of the board; only parent-child edges are used.</param>
    /// <param name="weights">Priority weights to use; null uses the default weights.</param>
    public static IReadOnlyList<CardPriority> Project(
        IReadOnlyList<Card> cards,
        IReadOnlyList<CardRelation> relations,
        PriorityWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(relations);

        var cardsById = cards.ToDictionary(card => card.Reference.CardId, StringComparer.Ordinal);
        var parentIdsByCard = relations
            .Where(relation => StringComparer.Ordinal.Equals(relation.Type, RelationTypes.ParentChild))
            .GroupBy(relation => relation.Target.CardId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(relation => relation.Source.CardId).ToArray(),
                StringComparer.Ordinal);

        var snapshots = new Dictionary<string, PrioritySnapshot>(StringComparer.Ordinal);
        var computing = new HashSet<string>(StringComparer.Ordinal);

        return cards
            .Select(card => new CardPriority(card.Reference.CardId, Compute(card)))
            .ToArray();

        PrioritySnapshot Compute(Card card)
        {
            if (snapshots.TryGetValue(card.Reference.CardId, out var memoized))
            {
                return memoized;
            }

            // Stories (and any non-task card) keep their own priority.
            if (card.Kind != CardKind.Task)
            {
                var own = new PrioritySnapshot(
                    card.OwnPriority,
                    null,
                    card.OwnPriority,
                    PriorityCalculator.CurrentFormulaVersion);
                snapshots[card.Reference.CardId] = own;
                return own;
            }

            // Guard against parent-child cycles: a card already being computed falls back to
            // its own priority, so pathological cycles terminate deterministically.
            if (!computing.Add(card.Reference.CardId))
            {
                var cyclic = new PrioritySnapshot(
                    card.OwnPriority,
                    null,
                    card.OwnPriority,
                    PriorityCalculator.CurrentFormulaVersion);
                snapshots[card.Reference.CardId] = cyclic;
                return cyclic;
            }

            var parentPriorities = new List<decimal>();
            if (parentIdsByCard.TryGetValue(card.Reference.CardId, out var parentIds))
            {
                foreach (var parentId in parentIds)
                {
                    if (cardsById.TryGetValue(parentId, out var parentCard))
                    {
                        parentPriorities.Add(Compute(parentCard).EffectivePriority);
                    }
                }
            }

            var snapshot = PriorityCalculator.CalculateTask(card.OwnPriority, parentPriorities, weights);
            snapshots[card.Reference.CardId] = snapshot;
            computing.Remove(card.Reference.CardId);
            return snapshot;
        }
    }
}
