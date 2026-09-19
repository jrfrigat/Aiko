namespace Aiko.Server.Contracts;

/// <summary>
/// Requests of the stage execution life cycle. Each one carries only what the coordinator method it feeds
/// needs, so a path cannot quietly grow a rule the coordinator does not have.
/// </summary>
/// <param name="StageId">Workflow stage to work.</param>
/// <param name="AgentAdapterId">Agent that takes the stage.</param>
internal sealed record StartExecutionRequest(string StageId, string AgentAdapterId);

/// <param name="Reason">Why the execution is paused; shown to whoever picks it up.</param>
internal sealed record PauseExecutionRequest(string Reason);

/// <param name="AgentAdapterId">Agent that resumes the execution.</param>
internal sealed record ResumeExecutionRequest(string AgentAdapterId);

/// <param name="TargetAgentAdapterId">Agent the execution is handed to.</param>
internal sealed record HandoffExecutionRequest(string TargetAgentAdapterId);

/// <param name="ActualChangedFiles">Files actually changed by the stage.</param>
/// <param name="Artifacts">Artifacts the stage produced.</param>
internal sealed record CompleteStageExecutionRequest(
    string[] ActualChangedFiles,
    string[] Artifacts);

/// <param name="Reason">Why the execution is cancelled; recorded with the cancelled attempt.</param>
internal sealed record CancelExecutionRequest(string? Reason);

/// <param name="RequestedScopeFiles">Files or globs asked for beyond the declared scope.</param>
/// <param name="Reason">Why the declared scope is not enough.</param>
internal sealed record RequestScopeExpansionRequest(string[] RequestedScopeFiles, string Reason);

/// <param name="Approved">True to accept the expansion, false to refuse it.</param>
/// <param name="AgentAdapterId">Agent that continues the work when the expansion is accepted.</param>
internal sealed record ScopeResponseRequest(bool Approved, string? AgentAdapterId);

/// <param name="Approved">True to approve the pending commit, false to reject it.</param>
internal sealed record CommitApprovalRequest(bool Approved);
