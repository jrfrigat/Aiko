using Aiko.Domain.Cards;
using Aiko.Domain.Execution;

namespace Aiko.Domain.Workflow;

/// <summary>
/// One card another card waits for, and where that card currently is.
/// </summary>
/// <param name="CardId">Id of the blocking card.</param>
/// <param name="Title">Its title, so a refusal can be repeated to a person unchanged.</param>
/// <param name="StageId">Stage it sits in now.</param>
/// <param name="StageTitle">That stage's title.</param>
public readonly record struct CardBlocker(string CardId, string Title, string StageId, string StageTitle);

/// <summary>
/// The rule that keeps a blocked card from being worked: a card waits for every card that blocks it.
/// </summary>
/// <remarks>
/// <c>blocks</c> is a directed edge and is stored like every other relation, but nothing read it: a blocked card
/// could be worked exactly as an unblocked one, and the order the user had expressed lived only in a file. The
/// rule is here rather than inside a tool for the same reason <see cref="CardProgress"/> is - it is about the
/// pipeline, not about one caller - and whether a caller applies it stays that caller's decision.
///
/// A blocker counts as finished by the one rule every screen asks, <see cref="CardCompletion.IsFinished"/>: it
/// sits in the last stage of its own pipeline and the latest run there completed. The pipelines differ per
/// card type, so the last stage is read from the card's workflow instead of being assumed to be called
/// <c>done</c>. An edge that points at a card the project does not have is not a blocker: the reindexer already
/// reports that defect, and a gate waiting for a card nobody can finish would stop the work for good.
/// </remarks>
public static class CardBlocking
{
    /// <summary>
    /// The blockers of <paramref name="card"/> whose work is not finished yet, by card id.
    /// </summary>
    /// <param name="card">The card that would be worked.</param>
    /// <param name="relations">All relations of the project, from which the incoming <c>blocks</c> edges come.</param>
    /// <param name="cards">All cards of the project, so a blocker can be named.</param>
    /// <param name="workflows">All workflows of the project, so the end of a pipeline can be read.</param>
    /// <param name="latestRunState">
    /// The state of the latest run of the stage a card sits in, or null when nothing is known about it.
    /// </param>
    public static IReadOnlyList<CardBlocker> Unfinished(
        CardReference card,
        IReadOnlyList<CardRelation> relations,
        IReadOnlyList<Card> cards,
        IReadOnlyList<WorkflowDefinition> workflows,
        Func<Card, StageExecutionState?> latestRunState)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(latestRunState);

        var blockers = new List<CardBlocker>();
        foreach (var relation in relations)
        {
            if (!StringComparer.Ordinal.Equals(relation.Type, RelationTypes.Blocks) ||
                !StringComparer.Ordinal.Equals(relation.Target.ProjectId, card.ProjectId) ||
                !StringComparer.Ordinal.Equals(relation.Target.CardId, card.CardId))
            {
                continue;
            }

            var blocker = cards.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Reference.ProjectId, relation.Source.ProjectId) &&
                StringComparer.Ordinal.Equals(candidate.Reference.CardId, relation.Source.CardId));
            if (blocker is null)
            {
                continue;
            }

            var workflow = workflows.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, blocker.WorkflowId));
            if (workflow is null || workflow.Stages.Count == 0)
            {
                continue;
            }

            var stage = workflow.Stages.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, blocker.StageId));
            if (stage is null || CardCompletion.IsFinished(workflow, stage.Id, latestRunState(blocker)))
            {
                // The blocker finished its pipeline, or sits in a stage this workflow does not have - in the
                // second case the card is not where its type says it can be, and guessing there would be worse
                // than not blocking.
                continue;
            }

            blockers.Add(new CardBlocker(blocker.Reference.CardId, blocker.Title, stage.Id, stage.Title));
        }

        return [.. blockers.OrderBy(blocker => blocker.CardId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why the card may not be started, or null when nothing blocks it.
    /// </summary>
    /// <param name="cardId">The card that would be worked, for the message.</param>
    /// <param name="blockers">Its unfinished blockers, from <see cref="Unfinished"/>.</param>
    public static string? RefuseStart(string cardId, IReadOnlyList<CardBlocker> blockers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentNullException.ThrowIfNull(blockers);
        if (blockers.Count == 0)
        {
            return null;
        }

        var list = string.Join(
            "; ",
            blockers.Select(blocker =>
                $"'{blocker.CardId}' ('{blocker.Title}'), which is in stage '{blocker.StageId}' " +
                $"('{blocker.StageTitle}') of its pipeline"));
        return $"card '{cardId}' is blocked: it waits for {list}. Do not start it - tell the user which card "
            + "blocks it and offer that card instead. The relation is the user's own order: if it no longer "
            + "holds, they remove it.";
    }
}
