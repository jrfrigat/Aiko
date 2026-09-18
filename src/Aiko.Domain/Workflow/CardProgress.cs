namespace Aiko.Domain.Workflow;

/// <summary>
/// The rule that keeps a card's progress honest: a card advances one stage at a time, and the stage it leaves
/// must have been run.
/// </summary>
/// <remarks>
/// Aiko cannot see an agent's edits - the agent works on the shared checkout with its own tools - so a stage
/// execution is the only trace that work happened. Without this rule a card can be declared finished by moving
/// it, and that is what such a card looks like on the board: it sits in a late stage with an empty runs tab,
/// no artifacts and no history, as if the pipeline had run itself. The rule lives here rather than inside a
/// tool because it is about the pipeline, not about one caller; whether a caller applies it is that caller's
/// decision.
/// </remarks>
public static class CardProgress
{
    /// <summary>
    /// Why the card may not move to <paramref name="targetStageId"/>, or null when the move is one the card
    /// has earned - a move backwards, or a forward step out of a stage that was run.
    /// </summary>
    /// <param name="cardId">Card being moved, for the message.</param>
    /// <param name="currentStageId">Stage the card is in now.</param>
    /// <param name="targetStageId">Stage it would move to.</param>
    /// <param name="stages">The card's workflow stages, in pipeline order.</param>
    /// <param name="executedStageIds">Stages the card has at least one execution for.</param>
    public static string? RefuseForwardMove(
        string cardId,
        string currentStageId,
        string targetStageId,
        IReadOnlyList<StageDefinition> stages,
        IReadOnlyCollection<string> executedStageIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(executedStageIds);

        var currentIndex = IndexOf(stages, currentStageId);
        var targetIndex = IndexOf(stages, targetStageId);
        if (currentIndex < 0 || targetIndex <= currentIndex)
        {
            // Backwards, in place, or a stage this pipeline cannot place. Pulling a card back is how rework
            // starts, so it is never this rule's business.
            return null;
        }

        if (targetIndex > currentIndex + 1)
        {
            return $"a card advances one stage at a time: '{cardId}' is in '{currentStageId}' and "
                + $"'{targetStageId}' is not the next stage of its pipeline. Start the stage you mean to work "
                + "with aiko_start_stage - the start moves the card into it - or move the card step by step.";
        }

        if (WorkflowDefinition.IsBacklog(stages[currentIndex]))
        {
            // The backlog is the list of cards nobody has taken into work yet: leaving it is how work begins,
            // and the start that follows is what records it.
            return null;
        }

        if (executedStageIds.Contains(currentStageId, StringComparer.Ordinal))
        {
            return null;
        }

        return $"stage '{currentStageId}' of card '{cardId}' has no execution: nothing was worked there, so "
            + "the card cannot move on. Start the stage with aiko_start_stage, do its work and complete it - a "
            + "card moves on because its stage is done, not because its stage is left.";
    }

    private static int IndexOf(IReadOnlyList<StageDefinition> stages, string stageId)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(stages[index].Id, stageId))
            {
                return index;
            }
        }

        return -1;
    }
}
