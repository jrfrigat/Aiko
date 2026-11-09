using Aiko.Domain.Cards;

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
/// <c>blocks</c> is a directed edge stored like every other relation, and the order the user expressed has to be
/// enforced rather than merely recorded: the rule is about the pipeline, so it lives here, and whether a caller
/// applies it stays that caller's decision.
///
/// A blocker that sits in the last stage of its own workflow has finished, whatever the latest run there says:
/// the stage is the pipeline's own end, and a card moved into it has arrived. Waiting for a closing run that
/// was never recorded is how a card in done kept holding the work behind it (TASK-377). An edge that points at
/// a card the project does not have - including one put away in the archive - is not a blocker: the reindexer
/// already reports that defect, and a gate waiting for a card nobody can finish would stop the work for good.
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
    public static IReadOnlyList<CardBlocker> Unfinished(
        CardReference card,
        IReadOnlyList<CardRelation> relations,
        IReadOnlyList<Card> cards,
        IReadOnlyList<WorkflowDefinition> workflows)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(workflows);

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
            if (stage is null || WorkflowDefinition.IsLastStage(workflow, stage))
            {
                // The blocker reached the end of its own pipeline, or sits in a stage this workflow does not
                // have - and in the second case guessing where the card is would be worse than not blocking.
                // The end is the pipeline position, not the run record: a card moved into done has arrived even
                // when its closing run was never completed, and waiting for that run would hold the work behind
                // it for good (TASK-377).
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
