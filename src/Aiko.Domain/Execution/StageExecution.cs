using Aiko.Domain.Cards;

namespace Aiko.Domain.Execution;

/// <summary>
/// Execution of a card stage: workspace, agent attempt history, progress
/// and the actually changed files.
/// </summary>
public sealed record StageExecution(
    string Id,
    CardReference Card,
    string StageId,
    WorkspaceMode WorkspaceMode,
    string WorkspacePath,
    IReadOnlyList<string> DeclaredScopeFiles,
    IReadOnlyList<string> ActualChangedFiles,
    IReadOnlyList<AgentAttempt> Attempts,
    DateTimeOffset CreatedAt,
    StageExecutionState State,
    string? ProgressSummary,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> RemainingSteps,
    IReadOnlyList<string> Artifacts,
    IReadOnlyList<string> RequestedScopeFiles,
    IReadOnlyList<Commit> Commits,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Last unclosed agent attempt, or null when every attempt has finished.
    /// </summary>
    public AgentAttempt? CurrentAttempt => Attempts.LastOrDefault(attempt =>
        attempt.State is AgentAttemptState.Queued or AgentAttemptState.Running or
            AgentAttemptState.WaitingForUser or AgentAttemptState.Paused);

    /// <summary>
    /// Files changed outside the declared scope (matched no pattern from
    /// <see cref="DeclaredScopeFiles"/> according to <see cref="ScopeMatcher"/>).
    /// </summary>
    public IReadOnlyList<string> OutOfScopeFiles =>
        ActualChangedFiles
            .Where(file => !DeclaredScopeFiles.Any(pattern => ScopeMatcher.Matches(pattern, file)))
            .ToArray();
}
