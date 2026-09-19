using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Prioritization;

/// <summary>
/// Projects effective card priorities from cards and their parent-child relations
/// using <see cref="PriorityCalculator"/>. In a parent-child relation the source card
/// is treated as the parent.
/// </summary>
public static class CardPriorityProjector
{
    /// <summary>
    /// Computes a <see cref="CardPriority"/> for every card: each card's own score is computed from its
    /// criterion values and its size (ТЗ §10), then a task blends it with the maximum parent value.
    /// </summary>
    /// <param name="cards">All cards of the board.</param>
    /// <param name="relations">All relations of the board; only parent-child edges are used.</param>
    /// <param name="settings">The project's priority settings: criteria, size grid and blending weights.</param>
    /// <param name="workflows">
    /// The project's card types. A card blends its own score with its parents' only when its type says so
    /// (<see cref="WorkflowDefinition.BlendsWithParent"/>), so the behaviour is read from data and never from
    /// the card's kind.
    /// </param>
    public static IReadOnlyList<CardPriority> Project(
        IReadOnlyList<Card> cards,
        IReadOnlyList<CardRelation> relations,
        PrioritySettings settings,
        IReadOnlyList<WorkflowDefinition> workflows)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workflows);

        var blendingWorkflows = workflows
            .Where(workflow => workflow.BlendsWithParent)
            .Select(workflow => workflow.Id)
            .ToHashSet(StringComparer.Ordinal);
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

            var ownScore = PriorityCalculator.CalculateOwnScore(
                card.OwnPriority,
                card.CriterionValues,
                settings,
                card.Size);

            // A card of a type that does not blend with its parents keeps its own score.
            if (!blendingWorkflows.Contains(card.WorkflowId))
            {
                var own = new PrioritySnapshot(
                    ownScore,
                    null,
                    ownScore,
                    PriorityCalculator.CurrentFormulaVersion);
                snapshots[card.Reference.CardId] = own;
                return own;
            }

            // Guard against parent-child cycles: a card already being computed falls back to
            // its own score, so pathological cycles terminate deterministically.
            if (!computing.Add(card.Reference.CardId))
            {
                var cyclic = new PrioritySnapshot(
                    ownScore,
                    null,
                    ownScore,
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

            var snapshot = PriorityCalculator.CalculateTask(ownScore, parentPriorities, settings.Weights);
            snapshots[card.Reference.CardId] = snapshot;
            computing.Remove(card.Reference.CardId);
            return snapshot;
        }
    }
}
