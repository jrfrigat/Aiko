using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
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
    ICardStore cards,
    IExecutionCoordinator executions) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_start_stage", Title = "Start Aiko stage")]
    [Description(
        "Starts a stage execution in the shared project workspace and records the responsible agent. Starting "
        + "the stage the card is already working in continues that execution - run the card again and the same "
        + "stage picks up where it stopped. Starting another stage while one is unfinished is refused: finish it "
        + "with aiko_complete_stage first. The stage must be one of the card's own pipeline, admit the agent, and be "
        + "the stage the card is in or the next one - a start moves the card, one stage at a time, and only out of a "
        + "finished stage. A card another card blocks is refused too: name the blocking card to "
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
        // The archive, the stage, the agent and the one-step rule are the coordinator's to judge, so the board's
        // start and this one cannot differ (TASK-165).

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
        return await AnswerAsync(execution, cancellationToken);
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
        return await AnswerAsync(execution, cancellationToken);
    }

    [McpServerTool(
        Name = "aiko_request_scope_expansion",
        Title = "Request Aiko scope expansion")]
    [Description(
        "Warns that work outside declaredScopeFiles is needed. What happens next is the project's own " +
        "scopeExpansionPolicy: ask (the default) pauses the run for the user, allow adds the files to the " +
        "card's declared scope and the run continues, and deny refuses - the call fails and nothing is " +
        "written, so stop and tell the user which files are needed.")]
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
        return await AnswerAsync(execution, cancellationToken);
    }

    [McpServerTool(Name = "aiko_complete_stage", Title = "Complete Aiko stage")]
    [Description(
        "Completes the current stage attempt and records actual files and produced artifacts. The card stays in "
        + "its stage - start the next stage with aiko_start_stage, or move it with aiko_move_card - and its revision "
        + "changes: the answer carries cardRevision for the next call. Refused while the card was not re-estimated "
        + "during this run: call aiko_estimate_card with the readiness criterion first.")]
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
        return await AnswerAsync(execution, cancellationToken);
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
        return await AnswerAsync(execution, cancellationToken);
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
        return await AnswerAsync(execution, cancellationToken);
    }

    [McpServerTool(Name = "aiko_resume_execution", Title = "Resume Aiko execution")]
    [Description(
        "Resumes a paused, waiting or needs-attention execution with a new AgentAttempt. A run that waits for "
        + "the user's decision - a commit to approve, or scope to grant - is refused: the user answers it on the "
        + "board, and the agent tells the user and stops.")]
    public async Task<string> ResumeExecutionAsync(
        [Description("Stage execution id.")]
        string executionId,
        [Description("Agent adapter id that resumes the execution.")]
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        if (await executions.FindAsync(executionId, cancellationToken) is { } waiting &&
            PendingUserDecision(waiting) is { } decision)
        {
            throw new InvalidOperationException(
                $"Run {executionId} is waiting for the user to decide on {decision}. The user answers on the "
                + "board; tell the user and stop here - an agent cannot answer for them.");
        }

        var execution = await executions.ResumeAsync(
            executionId,
            agentAdapterId,
            cancellationToken);
        return await AnswerAsync(execution, cancellationToken);
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
        return await AnswerAsync(execution, cancellationToken);
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
        return await AnswerAsync(execution, cancellationToken);
    }

    /// <summary>
    /// What a run waits for the user to decide, or null when it waits for nothing the user owns. A commit the
    /// project asks the user to approve, and scope the project asks the user to grant, are the user's answers:
    /// an agent that could give them itself would turn "ask" into "allow". There is no approve tool for the same
    /// reason - the board's REST surface is where the user answers.
    /// </summary>
    private static string? PendingUserDecision(StageExecution execution)
    {
        if (execution.State != StageExecutionState.WaitingForUser)
        {
            return null;
        }

        if (execution.Commits.Any(commit => commit.State == CommitState.PendingApproval))
        {
            return "a commit";
        }

        return execution.RequestedScopeFiles.Except(execution.DeclaredScopeFiles, StringComparer.Ordinal).Any()
            ? "a scope request"
            : null;
    }

    /// <summary>
    /// The execution, and the card as it is after it. Starting, completing and scope requests change the card's
    /// revision, and an agent that only got the execution back named the old one in its next estimate or move -
    /// so the answer carries the revision and the stage the card has now.
    /// </summary>
    private async Task<string> AnswerAsync(StageExecution execution, CancellationToken cancellationToken)
    {
        var answer = JsonNode.Parse(SerializeExecution(execution))!.AsObject();
        if (await cards.FindAsync(execution.Card, cancellationToken) is { } card)
        {
            answer["cardRevision"] = card.Revision;
            answer["cardStageId"] = card.StageId;
        }

        return answer.ToJsonString();
    }
}
