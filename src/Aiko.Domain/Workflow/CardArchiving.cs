using Aiko.Domain.Cards;
using Aiko.Domain.Execution;

namespace Aiko.Domain.Workflow;

/// <summary>
/// The rule that keeps the archive honest: only a card that has finished its pipeline may be put away, and a
/// card in the archive is not worked.
/// </summary>
/// <remarks>
/// The archive is a reading of a card rather than a stage it moves to, so nothing about the pipeline changes
/// when a card is put away - which is exactly why the gate has to exist. Without it the archive would accept a
/// card mid-flight, and the board would lose sight of work someone is in the middle of. The rule lives here
/// beside <see cref="CardProgress"/> and <see cref="CardBlocking"/>, takes the data it judges and returns the
/// text of its refusal; whether a caller applies it stays that caller's decision.
/// <para>
/// A card in the archive is off the board, so it cannot block anything on it, and the archive does not have to
/// refuse cards that block others: <see cref="CardBlocking"/> already treats a card at the end of its pipeline
/// as finished rather than as a blocker, and this gate accepts nothing but such a card. The two rules hold
/// together, and a test says so.
/// </para>
/// </remarks>
public static class CardArchiving
{
    /// <summary>
    /// Whether a stage's latest run left the stage open: someone is working there, or it was left hanging.
    /// </summary>
    /// <remarks>
    /// Judged per stage and only on its latest run. An old failed attempt followed by a successful one leaves a
    /// stage that <see cref="CardProgress"/> is willing to move on from, and the archive must not be stricter
    /// than the pipeline it is about.
    /// </remarks>
    private static bool IsOpen(StageExecutionState state) =>
        state is not (StageExecutionState.Completed or StageExecutionState.Cancelled);

    /// <summary>
    /// Why the card may not be put into the archive, or null when it may.
    /// </summary>
    /// <param name="card">The card that would be put away.</param>
    /// <param name="workflow">Its workflow, or null when the project has none for its type.</param>
    /// <param name="latestRuns">
    /// The latest run of each stage of the card, as the coordinator reports them. Stages never started have no
    /// entry, which is not an open run - nothing was left hanging.
    /// </param>
    public static string? Refuse(
        Card card,
        WorkflowDefinition? workflow,
        IReadOnlyList<StageRun> latestRuns)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(latestRuns);

        // Answered first, and on purpose: a card already in the archive is finished by definition, so every
        // other check would answer a repeated action with the wrong reason.
        if (card.IsArchived)
        {
            return $"card '{card.Reference.CardId}' is already in the archive: it is off the board. Return it "
                + "with aiko_restore_card (or the archive view of the board) if you meant to work on it.";
        }

        foreach (var run in latestRuns)
        {
            if (IsOpen(run.State))
            {
                return $"card '{card.Reference.CardId}' has an open run: stage '{run.StageId}' stopped in "
                    + $"state {run.State}. Finish that stage - complete it with aiko_complete_stage, or cancel "
                    + "it - before putting the card away: the archive must not hide work someone is in the "
                    + "middle of.";
            }
        }

        var currentRun = latestRuns
            .Where(run => StringComparer.Ordinal.Equals(run.StageId, card.StageId))
            .Select(run => (StageRun?)run)
            .FirstOrDefault();
        if (!CardCompletion.IsFinished(workflow, card.StageId, currentRun?.State))
        {
            var lastStage = workflow?.Stages
                .OrderByDescending(stage => stage.Order)
                .FirstOrDefault();
            var where = lastStage is null
                ? $"stage '{card.StageId}'"
                : $"stage '{card.StageId}', which is not the last stage ('{lastStage.Id}') of its pipeline";
            return $"card '{card.Reference.CardId}' is not finished: it sits in {where} and its latest run "
                + $"there is {Describe(currentRun?.State)}. The archive is for cards that reached the end of "
                + "their own pipeline: work the card to its last stage, complete it there, and put it away then.";
        }

        return null;
    }

    /// <summary>
    /// Why the card may not be taken into work - a stage started on it, or the card moved - or null when it may.
    /// </summary>
    /// <param name="card">The card that would be worked.</param>
    public static string? RefuseWork(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.IsArchived
            ? $"card '{card.Reference.CardId}' is in the archive: it is off the board and out of its pipeline, "
                + "so no stage of it may be started and it may not be moved. Return it from the archive first "
                + "with aiko_restore_card."
            : null;
    }

    /// <summary>
    /// The cards a board shows: everything that is not in the archive.
    /// </summary>
    /// <remarks>
    /// The one place the filter is written. A board, the work queue and the card list an agent reads all ask
    /// this, so "the archive is off the board" is one expression rather than three that can drift apart. The
    /// store keeps answering with every card, because the id generator and the doctor must still see what is
    /// archived - they would hand out a taken id otherwise.
    /// </remarks>
    /// <param name="cards">Every card of the project.</param>
    public static IReadOnlyList<Card> OnBoard(IReadOnlyList<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return [.. cards.Where(card => !card.IsArchived)];
    }

    /// <summary>The state of a run for a message, or <c>not started</c> when there is no run at all.</summary>
    private static string Describe(StageExecutionState? state) =>
        state?.ToString() ?? "not started";
}
