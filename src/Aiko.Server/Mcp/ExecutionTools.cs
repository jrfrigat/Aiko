using System.ComponentModel;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for the stage execution life cycle: starting, progress reporting,
/// scope expansion, completion, pausing, handoff, resuming and agent states.
/// </summary>
[McpServerToolType]
internal sealed class ExecutionTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    ICardBlockers blockers,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_start_stage", Title = "Start Aiko stage")]
    [Description(
        "Starts a stage execution in the shared project workspace and records the responsible agent. Starting "
        + "the stage the card is already working in continues that execution - run the card again and the same "
        + "stage picks up where it stopped. Starting another stage while one is unfinished is refused: finish it "
        + "with aiko_complete_stage first. A card another card blocks is refused too: name the blocking card to "
        + "the user and offer that card instead of working this one.")]
    public async Task<string> StartStageAsync(
        [Description("Card id.")]
        string cardId,
        [Description("Workflow stage id.")]
        string stageId,
        [Description("Agent adapter id.")]
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        // Work waits for the cards that block it. `blocks` is the user's own order, and until this gate existed
        // it lived only in relations.json: a blocked card started exactly like a free one, which is how a card
        // was worked while the card it waited for sat in the backlog. The refusal names the blocker, because the
        // agent's next move is to say so to the user and offer that card - not to retry.
        var card = new CardReference(GetProjectId(), cardId);
        var blockedBy = await blockers.UnfinishedAsync(card, cancellationToken);
        if (CardBlocking.RefuseStart(cardId, blockedBy) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        var execution = await executions.StartAsync(
            card,
            stageId,
            agentAdapterId,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_report_progress", Title = "Report Aiko progress")]
    [Description(
        "Updates execution progress, completed/remaining steps and the complete current changed-file list.")]
    public async Task<string> ReportProgressAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Concise progress summary suitable for handoff.")]
        string summary,
        [Description("Completed work items.")]
        string[] completedSteps,
        [Description("Remaining work items.")]
        string[] remainingSteps,
        [Description("Complete list of files changed so far.")]
        string[] actualChangedFiles,
        CancellationToken cancellationToken)
    {
        var execution = await executions.ReportProgressAsync(
            executionId,
            summary,
            completedSteps,
            remainingSteps,
            actualChangedFiles,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(
        Name = "aiko_request_scope_expansion",
        Title = "Request Aiko scope expansion")]
    [Description(
        "Warns that work outside declaredScopeFiles is needed and pauses for user review.")]
    public async Task<string> RequestScopeExpansionAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Files or glob patterns requested in addition to declared scope.")]
        string[] requestedScopeFiles,
        [Description("Why the additional files are necessary.")]
        string reason,
        CancellationToken cancellationToken)
    {
        var execution = await executions.RequestScopeExpansionAsync(
            executionId,
            requestedScopeFiles,
            reason,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_complete_stage", Title = "Complete Aiko stage")]
    [Description(
        "Completes the current stage attempt and records actual files and produced artifacts.")]
    public async Task<string> CompleteStageAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Complete actual changed-file list.")]
        string[] actualChangedFiles,
        [Description("Produced artifact paths relative to the card directory.")]
        string[] artifacts,
        CancellationToken cancellationToken)
    {
        var execution = await executions.CompleteAsync(
            executionId,
            actualChangedFiles,
            artifacts,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_pause_execution", Title = "Pause Aiko execution")]
    [Description("Pauses an active stage execution while preserving its workspace and history.")]
    public async Task<string> PauseExecutionAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Reason for pausing.")]
        string reason,
        CancellationToken cancellationToken)
    {
        var execution = await executions.PauseAsync(
            executionId,
            reason,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_handoff_execution", Title = "Handoff Aiko execution")]
    [Description(
        "Hands the same StageExecution and workspace to another agent and creates a new AgentAttempt.")]
    public async Task<string> HandoffExecutionAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Target agent adapter id, for example codex.")]
        string targetAgentAdapterId,
        CancellationToken cancellationToken)
    {
        var execution = await executions.HandoffAsync(
            executionId,
            targetAgentAdapterId,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_resume_execution", Title = "Resume Aiko execution")]
    [Description(
        "Resumes a paused, waiting or needs-attention execution with a new AgentAttempt.")]
    public async Task<string> ResumeExecutionAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Agent adapter id that resumes the execution.")]
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        var execution = await executions.ResumeAsync(
            executionId,
            agentAdapterId,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_report_agent_state", Title = "Report Aiko agent state")]
    [Description(
        "Reports an agent attempt state. Use rate-limited before handing the card to another agent.")]
    public async Task<string> ReportAgentStateAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description(
            "State: queued, running, waiting-for-user, rate-limited, paused, failed, cancelled, superseded or completed.")]
        string state,
        [Description("Useful exit reason or last error; pass null when absent.")]
        string? exitReason,
        CancellationToken cancellationToken)
    {
        var execution = await executions.ReportAgentStateAsync(
            executionId,
            ParseAgentState(state),
            exitReason,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_report_commit", Title = "Report Aiko commit")]
    [Description(
        "Reports a Git commit for the current execution. Honors the project commit policy: allow records it, ask records a pending request awaiting user approval, deny is rejected.")]
    public async Task<string> ReportCommitAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Commit SHA, or null when the commit is not created yet (ask policy).")]
        string? commitSha,
        [Description("Commit message.")]
        string message,
        [Description("Files included in the commit.")]
        string[] files,
        CancellationToken cancellationToken)
    {
        var execution = await executions.ReportCommitAsync(
            executionId,
            commitSha,
            message,
            files,
            cancellationToken);
        return SerializeExecution(execution);
    }

    [McpServerTool(Name = "aiko_approve_commit", Title = "Approve Aiko commit")]
    [Description(
        "Approves or rejects the pending commit request of the current execution and resumes it.")]
    public async Task<string> ApproveCommitAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("True to approve, false to reject the pending commit.")]
        bool approved,
        CancellationToken cancellationToken)
    {
        var execution = await executions.ApproveCommitAsync(executionId, approved, cancellationToken);
        return SerializeExecution(execution);
    }
}
