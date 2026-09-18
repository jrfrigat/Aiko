using Aiko.Domain.Execution;

namespace Aiko.Pwa.Services;

/// <summary>
/// Where a card's current stage got to, in the words the screens use - the five states of the workflow.
/// </summary>
public enum StageState
{
    /// <summary>No run of this stage exists yet: nobody has started it.</summary>
    Pending,

    /// <summary>An agent is working it.</summary>
    Running,

    /// <summary>It was started and stopped: paused by the user or by the agent, before the work was done.</summary>
    Paused,

    /// <summary>An agent asked the user something and waits for the answer.</summary>
    WaitingForUser,

    /// <summary>The last run ended badly - an agent failure or its rate limit - which is its own reason.</summary>
    Blocked,

    /// <summary>The stage is finished, so the card may move on.</summary>
    Completed
}

/// <summary>
/// Turns the runs of a stage into the state a screen shows, and counts the stages that are finished.
/// </summary>
/// <remarks>
/// Derived, never stored: the executions are the only truth about what a stage did, and a screen that kept its
/// own copy of "where is this card" would sooner or later disagree with the runs tab beside it. The board sends
/// the runs, the card page already has them, and both go through here so the two screens cannot differ.
/// </remarks>
public static class StageStateView
{
    /// <summary>The state of a stage from the runs it has, oldest first.</summary>
    /// <remarks>
    /// A finished run wins over everything else: a stage that was finished and then started again is still
    /// finished work, and calling it running would make the card look unfinished while it waits to move on.
    /// </remarks>
    public static StageState Of(IEnumerable<StageExecutionState> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var states = runs.Select(Of).ToArray();
        return states.Length == 0
            ? StageState.Pending
            : states.Contains(StageState.Completed) ? StageState.Completed : states[^1];
    }

    /// <summary>The same, for the board's runs, whose state travels as text.</summary>
    public static StageState Of(IEnumerable<string?> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var parsed = states.Select(Of).ToArray();
        return parsed.Length == 0
            ? StageState.Pending
            : parsed.Contains(StageState.Completed) ? StageState.Completed : parsed[^1];
    }

    /// <summary>One run's state, as a screen shows it.</summary>
    public static StageState Of(StageExecutionState state) => state switch
    {
        StageExecutionState.Running => StageState.Running,
        StageExecutionState.Paused => StageState.Paused,
        StageExecutionState.WaitingForUser => StageState.WaitingForUser,
        StageExecutionState.NeedsAttention => StageState.Blocked,
        StageExecutionState.Completed => StageState.Completed,
        _ => StageState.Pending
    };

    /// <summary>
    /// One run's state read from the text the board carries. A value this client cannot read counts as
    /// "nothing known", which is the honest reading of a state it does not understand.
    /// </summary>
    public static StageState Of(string? state) =>
        Enum.TryParse<StageExecutionState>(state, ignoreCase: false, out var parsed)
            ? Of(parsed)
            : StageState.Pending;

    /// <summary>How many of a card's stages have a finished run.</summary>
    public static int FinishedStages(IReadOnlyList<StageExecution> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);
        return executions
            .Where(execution => execution.State == StageExecutionState.Completed)
            .Select(execution => execution.StageId)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>The resource key of a state's label.</summary>
    public static string LabelKey(StageState state) => state switch
    {
        StageState.Running => "StageStateRunning",
        StageState.Paused => "StageStatePaused",
        StageState.WaitingForUser => "StageStateWaiting",
        StageState.Blocked => "StageStateBlocked",
        StageState.Completed => "StageStateDone",
        _ => "StageStatePending"
    };

    /// <summary>The tag class a state is drawn with, from the tags the design already has.</summary>
    public static string TagClass(StageState state) => state switch
    {
        StageState.Running => "aiko-tag--live",
        StageState.Completed => "aiko-tag--accent",
        StageState.Paused or StageState.Blocked => "aiko-tag--warn",
        _ => "aiko-tag--quiet"
    };
}
