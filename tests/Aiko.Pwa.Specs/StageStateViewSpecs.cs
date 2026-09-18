using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards how a stage's runs become the state a screen shows - the five states the owner asked to see.
/// </summary>
public sealed class StageStateViewSpecs
{
    [Fact]
    public void No_run_means_the_stage_is_waiting()
    {
        Assert.Equal(StageState.Pending, StageStateView.Of(Array.Empty<StageExecutionState>()));
        Assert.Equal(StageState.Pending, StageStateView.Of(Array.Empty<string?>()));
        // A state this client cannot read is not invented either.
        Assert.Equal(StageState.Pending, StageStateView.Of("SomethingNew"));
        Assert.Equal(StageState.Pending, StageStateView.Of((string?)null));
    }

    [Fact]
    public void Each_state_a_run_can_be_in_reads_as_its_own_word()
    {
        Assert.Equal(StageState.Running, StageStateView.Of(StageExecutionState.Running));
        Assert.Equal(StageState.Paused, StageStateView.Of(StageExecutionState.Paused));
        Assert.Equal(StageState.WaitingForUser, StageStateView.Of(StageExecutionState.WaitingForUser));
        // A failure or a rate limit is a pause with a reason, and the screens say which.
        Assert.Equal(StageState.Blocked, StageStateView.Of(StageExecutionState.NeedsAttention));
        Assert.Equal(StageState.Completed, StageStateView.Of(StageExecutionState.Completed));
        Assert.Equal(StageState.Pending, StageStateView.Of(StageExecutionState.Cancelled));
    }

    [Fact]
    public void The_state_travels_as_text_over_the_board()
    {
        Assert.Equal(StageState.Running, StageStateView.Of("Running"));
        Assert.Equal(StageState.Blocked, StageStateView.Of("NeedsAttention"));
        Assert.Equal(StageState.Completed, StageStateView.Of("Completed"));
    }

    [Fact]
    public void A_finished_stage_stays_finished()
    {
        // The last run is running again, but the stage has already produced its outcome: calling it running
        // would make the card look unfinished while it waits to move on.
        Assert.Equal(
            StageState.Completed,
            StageStateView.Of([StageExecutionState.Completed, StageExecutionState.Running]));
        // Without a finished run the last word wins: the stage was paused and then picked up again.
        Assert.Equal(
            StageState.Running,
            StageStateView.Of([StageExecutionState.Paused, StageExecutionState.Running]));
        Assert.Equal(
            StageState.WaitingForUser,
            StageStateView.Of(["FailedToParse", "WaitingForUser"]));
    }

    [Fact]
    public void Finished_stages_are_counted_once_each()
    {
        Assert.Equal(0, StageStateView.FinishedStages([]));
        Assert.Equal(
            2,
            StageStateView.FinishedStages(
            [
                Execution("analysis", StageExecutionState.Completed),
                Execution("analysis", StageExecutionState.Running),
                Execution("implementation", StageExecutionState.Completed),
                Execution("review", StageExecutionState.Paused)
            ]));
    }

    [Fact]
    public void Every_state_has_its_own_label_and_tag()
    {
        var states = Enum.GetValues<StageState>();
        // Every state has its own word...
        Assert.Equal(states.Length, states.Select(StageStateView.LabelKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(states, state => Assert.StartsWith("StageState", StageStateView.LabelKey(state)));
        // ...and a tag from the design's own vocabulary. Quiet is shared on purpose - a stage waiting to be
        // started and one waiting for a decision both read as "nothing is happening right now" - so only the
        // states a reader must not mistake for that are pinned.
        Assert.All(states, state => Assert.StartsWith("aiko-tag--", StageStateView.TagClass(state)));
        Assert.Equal("aiko-tag--live", StageStateView.TagClass(StageState.Running));
        Assert.Equal("aiko-tag--accent", StageStateView.TagClass(StageState.Completed));
        Assert.Equal("aiko-tag--warn", StageStateView.TagClass(StageState.Blocked));
        Assert.Equal("aiko-tag--warn", StageStateView.TagClass(StageState.Paused));
    }

    private static StageExecution Execution(string stageId, StageExecutionState state) => new(
        Guid.NewGuid().ToString("N"),
        new CardReference("project", "TASK-1"),
        stageId,
        WorkspaceMode.Shared,
        @"C:\work",
        [],
        [],
        [],
        DateTimeOffset.UtcNow,
        state,
        null,
        [],
        [],
        [],
        [],
        [],
        DateTimeOffset.UtcNow);
}
