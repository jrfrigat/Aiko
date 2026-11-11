using System.Runtime.InteropServices;
using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Prioritization;

/// <summary>
/// Projects effective card priorities from cards and their parent-child relations using
/// <see cref="PriorityCalculator"/>; in that relation the source card is the parent.
/// </summary>
public static class CardPriorityProjector
{
    /// <summary>Computes a <see cref="CardPriority"/> for every card: the own score, then the blend with its parents.</summary>
    /// <param name="cards">All cards of the board.</param>
    /// <param name="relations">All relations of the board; only parent-child edges are used.</param>
    /// <param name="settings">The project's priority settings: criteria, size grid and blending weights.</param>
    /// <param name="workflows">
    /// The project's card types. A card blends its own score with its parents' only when its type says so
    /// (<see cref="WorkflowDefinition.BlendsWithParent"/>), never because of its kind.
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

        // The board is rebuilt on every screen load, so the lookups are sized once and filled without the
        // intermediate LINQ collections a Where/GroupBy chain leaves behind.
        var blendingWorkflows = new HashSet<string>(workflows.Count, StringComparer.Ordinal);
        for (var index = 0; index < workflows.Count; index++)
        {
            var workflow = workflows[index];
            if (workflow.BlendsWithParent)
            {
                blendingWorkflows.Add(workflow.Id);
            }
        }

        var cardsById = new Dictionary<string, Card>(cards.Count, StringComparer.Ordinal);
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            // Add, not the indexer: two cards under one id are a broken board, and the previous dictionary
            // said so instead of quietly keeping the last one.
            cardsById.Add(card.Reference.CardId, card);
        }

        var parentIdsByCard = new Dictionary<string, List<string>>(
            Math.Min(relations.Count, cards.Count),
            StringComparer.Ordinal);
        for (var index = 0; index < relations.Count; index++)
        {
            var relation = relations[index];
            if (!StringComparer.Ordinal.Equals(relation.Type, RelationTypes.ParentChild))
            {
                continue;
            }

            ref var parentIds = ref CollectionsMarshal.GetValueRefOrAddDefault(
                parentIdsByCard,
                relation.Target.CardId,
                out var exists);
            if (!exists)
            {
                parentIds = new List<string>(1);
            }

            parentIds!.Add(relation.Source.CardId);
        }

        var snapshots = new Dictionary<string, PrioritySnapshot>(cards.Count, StringComparer.Ordinal);
        var computing = new HashSet<string>(cards.Count, StringComparer.Ordinal);
        var priorities = new CardPriority[cards.Count];
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            priorities[index] = new CardPriority(card.Reference.CardId, Compute(card));
        }

        return priorities;

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

            decimal? maximumParent = null;
            if (parentIdsByCard.TryGetValue(card.Reference.CardId, out var parentIds))
            {
                for (var index = 0; index < parentIds.Count; index++)
                {
                    if (cardsById.TryGetValue(parentIds[index], out var parentCard))
                    {
                        var parentPriority = Compute(parentCard).EffectivePriority;
                        if (maximumParent is null || parentPriority > maximumParent)
                        {
                            maximumParent = parentPriority;
                        }
                    }
                }
            }

            var snapshot = PriorityCalculator.CalculateTaskFromMaximum(
                ownScore,
                maximumParent,
                settings.Weights);
            snapshots[card.Reference.CardId] = snapshot;
            computing.Remove(card.Reference.CardId);
            return snapshot;
        }
    }
}
