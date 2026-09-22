using Aiko.Domain.Execution;

namespace Aiko.Domain.Workflow;

/// <summary>
/// The rule that says a card has reached the end of its own pipeline.
/// </summary>
/// <remarks>
/// "Finished" used to be assembled inside the work queue, as one expression: the card sits in the last stage of
/// its workflow and that stage's latest run completed. It is a fact about the pipeline rather than about the
/// queue, so it is stated here - the queue hides finished cards and the archive accepts nothing else, and two
/// places that each decide what "finished" means is how one of them starts hiding a card the other still calls
/// work. The last stage is read from the card's own workflow, because pipelines differ per card type and calling
/// the end <c>done</c> is only true of the workflows that happen to use that id.
/// </remarks>
public static class CardCompletion
{
    /// <summary>
    /// Whether a card in <paramref name="stageId"/> has finished its pipeline.
    /// </summary>
    /// <param name="workflow">
    /// The card's workflow, or null when the project has none for it. A card whose type has no pipeline is not
    /// finished, because there is no end for it to have reached.
    /// </param>
    /// <param name="stageId">Stage the card sits in now.</param>
    /// <param name="latestRunState">
    /// State of the latest run of that stage, or null when nothing is known about it. Nothing known is not
    /// finished: a card that was never worked is the clearest case of work left to do.
    /// </param>
    public static bool IsFinished(
        WorkflowDefinition? workflow,
        string stageId,
        StageExecutionState? latestRunState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        if (workflow is null)
        {
            return false;
        }

        var stage = workflow.Stages.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, stageId));
        return stage is not null &&
               WorkflowDefinition.IsLastStage(workflow, stage) &&
               latestRunState == StageExecutionState.Completed;
    }
}
