using Aiko.Domain.Cards;
using Aiko.Domain.Execution;

namespace Aiko.Application.Contracts;

/// <summary>
/// Coordinator of stage executions: the StageExecution life cycle,
/// agent attempts, progress, scope expansion and handoffs.
/// </summary>
public interface IExecutionCoordinator
{
    /// <summary>
    /// Returns all stage executions of the card in creation order.
    /// </summary>
    ValueTask<IReadOnlyList<StageExecution>> ListAsync(
        CardReference card,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds an execution by identifier or returns null.
    /// </summary>
    ValueTask<StageExecution?> FindAsync(
        string executionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Where each stage of each card got to: the latest run per (card, stage) pair, for the screens that show a
    /// card's progress without loading its whole history.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<StageRunSummary>> ReadStageRunsAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts a new stage execution of the card for the given agent;
    /// parallel active executions of one card are rejected.
    /// </summary>
    ValueTask<StageExecution> StartAsync(
        CardReference card,
        string stageId,
        string agentAdapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands the execution to another agent: writes a handoff document,
    /// closes the current attempt and opens a new one.
    /// </summary>
    ValueTask<StageExecution> HandoffAsync(
        string executionId,
        string targetAgentAdapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records progress: summary, completed and remaining steps,
    /// and the full list of changed files.
    /// </summary>
    ValueTask<StageExecution> ReportProgressAsync(
        string executionId,
        string summary,
        IReadOnlyList<string> completedSteps,
        IReadOnlyList<string> remainingSteps,
        IReadOnlyList<string> actualChangedFiles,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests a declared-scope expansion: what happens next is the project's own
    /// <c>scopeExpansionPolicy</c> - the run waits for the user, the files join the card's declared scope and
    /// the run keeps going, or the request is refused.
    /// </summary>
    ValueTask<StageExecution> RequestScopeExpansionAsync(
        string executionId,
        IReadOnlyList<string> requestedScopeFiles,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes the execution: records the actual files and artifacts,
    /// updates the card and closes the attempt.
    /// </summary>
    ValueTask<StageExecution> CompleteAsync(
        string executionId,
        IReadOnlyList<string> actualChangedFiles,
        IReadOnlyList<string> artifacts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pauses the execution, preserving the workspace and history.
    /// </summary>
    ValueTask<StageExecution> PauseAsync(
        string executionId,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resumes a paused or waiting execution with a new agent attempt.
    /// </summary>
    ValueTask<StageExecution> ResumeAsync(
        string executionId,
        string agentAdapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports the state of the current agent attempt (including rate-limited)
    /// and synchronizes the execution state with it.
    /// </summary>
    ValueTask<StageExecution> ReportAgentStateAsync(
        string executionId,
        AgentAttemptState state,
        string? exitReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports a Git commit for the execution, honoring the project's commit policy:
    /// allow - recorded, ask - recorded as pending approval, deny - rejected.
    /// </summary>
    ValueTask<StageExecution> ReportCommitAsync(
        string executionId,
        string? commitSha,
        string message,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken);

    /// <summary>
    /// Approves or rejects the pending commit request of the execution and resumes it.
    /// </summary>
    ValueTask<StageExecution> ApproveCommitAsync(
        string executionId,
        bool approved,
        CancellationToken cancellationToken);
}
