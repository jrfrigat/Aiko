using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The execution life cycle over HTTP: starting a stage, pausing, resuming, handing off, completing and
/// cancelling a run, answering a scope request and approving a commit.
/// </summary>
/// <remarks>
/// Every path translates an <see cref="IExecutionCoordinator"/> call - the rules stay in the coordinator, so
/// the interface cannot drift from the agent's tools. Two paths have no method of their own and say so where
/// they are defined: cancelling is the coordinator's cancelled agent state, and refusing a scope expansion is
/// a cancellation with a reason.
/// </remarks>
internal static class ExecutionEndpoints
{
    /// <summary>
    /// Maps the execution routes.
    /// </summary>
    public static void MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/executions/{executionId}",
            async (
                string executionId,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
                await executions.FindAsync(executionId, cancellationToken) is { } execution
                    ? Results.Ok(execution)
                    : Results.NotFound());
        app.MapPost(
            "/api/v1/projects/{projectId}/cards/{cardId}/executions",
            async (
                string projectId,
                string cardId,
                StartExecutionRequest request,
                ICardBlockers blockers,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.StageId) || string.IsNullOrWhiteSpace(request.AgentAdapterId))
                {
                    return Results.BadRequest(new ErrorResponse("A stage and an agent are both required."));
                }

                var card = new CardReference(projectId, cardId);
                return await RunAsync(async () =>
                {
                    // The same gate the MCP tool passes: work waits for the cards that block it, and the refusal
                    // names the blocker. Skipping it here would let the interface start a card the agent is
                    // refused - the very difference this endpoint exists to avoid.
                    var blockedBy = await blockers.UnfinishedAsync(card, cancellationToken);
                    if (CardBlocking.RefuseStart(cardId, blockedBy) is { } refusal)
                    {
                        throw new InvalidOperationException(refusal);
                    }

                    return await executions.StartAsync(
                        card,
                        request.StageId,
                        request.AgentAdapterId,
                        cancellationToken);
                });
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/pause",
            async (
                string executionId,
                PauseExecutionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Results.BadRequest(new ErrorResponse("A pause needs a reason."));
                }

                return await RunAsync(async () =>
                    await executions.PauseAsync(executionId, request.Reason, cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/resume",
            async (
                string executionId,
                ResumeExecutionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.AgentAdapterId))
                {
                    return Results.BadRequest(new ErrorResponse("An agent is required to resume."));
                }

                return await RunAsync(async () =>
                    await executions.ResumeAsync(executionId, request.AgentAdapterId, cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/handoff",
            async (
                string executionId,
                HandoffExecutionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.TargetAgentAdapterId))
                {
                    return Results.BadRequest(new ErrorResponse("A target agent is required for a handoff."));
                }

                return await RunAsync(async () => await executions.HandoffAsync(
                    executionId,
                    request.TargetAgentAdapterId,
                    cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/complete",
            async (
                string executionId,
                CompleteStageExecutionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await executions.CompleteAsync(
                    executionId,
                    request.ActualChangedFiles ?? [],
                    request.Artifacts ?? [],
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/cancel",
            async (
                string executionId,
                CancelExecutionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
                // There is no CancelAsync: a cancelled agent attempt is what moves the execution to Cancelled,
                // so this path reports that state rather than inventing a second way to stop a run.
                await RunAsync(async () => await executions.ReportAgentStateAsync(
                    executionId,
                    AgentAttemptState.Cancelled,
                    string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by the user." : request.Reason,
                    cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/scope-expansion",
            async (
                string executionId,
                RequestScopeExpansionRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (request.RequestedScopeFiles is not { Length: > 0 } || string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Results.BadRequest(new ErrorResponse(
                        "A scope request needs at least one file and a reason."));
                }

                return await RunAsync(async () => await executions.RequestScopeExpansionAsync(
                    executionId,
                    request.RequestedScopeFiles,
                    request.Reason,
                    cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/scope-response",
            async (
                string executionId,
                ScopeResponseRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
            {
                if (!request.Approved)
                {
                    // Refusing the expansion means the work cannot be done inside the declared scope, so the run
                    // is cancelled with that reason: leaving it waiting for a person who has already answered
                    // would show a wait nobody is in.
                    return await RunAsync(async () => await executions.ReportAgentStateAsync(
                        executionId,
                        AgentAttemptState.Cancelled,
                        "The requested scope expansion was refused.",
                        cancellationToken));
                }

                if (string.IsNullOrWhiteSpace(request.AgentAdapterId))
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Accepting a scope request needs the agent that continues the work."));
                }

                return await RunAsync(async () =>
                    await executions.ResumeAsync(executionId, request.AgentAdapterId, cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/executions/{executionId}/commit-approval",
            async (
                string executionId,
                CommitApprovalRequest request,
                IExecutionCoordinator executions,
                CancellationToken cancellationToken) =>
                await RunAsync(async () => await executions.ApproveCommitAsync(
                    executionId,
                    request.Approved,
                    cancellationToken)));
    }

    /// <summary>
    /// Runs a coordinator call and turns its outcome into a response: a missing execution is a 404, a refusal
    /// the coordinator names is a 409 carrying that text, bad input is a 400, and success is the execution.
    /// </summary>
    private static async Task<IResult> RunAsync(Func<Task<StageExecution>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (InvalidOperationException exception)
        {
            // The coordinator refuses the transition (unfinished stage, a second run of the same card, wrong
            // state). Its own words are what the person needs, so they travel untouched.
            return Results.Conflict(new ErrorResponse(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ErrorResponse(exception.Message));
        }
    }
}
