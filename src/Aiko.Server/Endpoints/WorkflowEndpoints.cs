using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;
using Aiko.Server.Security;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Workflow endpoints: creating, updating and removing card types with optimistic revisions.
/// </summary>
internal static class WorkflowEndpoints
{
    /// <summary>
    /// Maps the workflow create, update and delete routes.
    /// </summary>
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        // A workflow is a card type. Creating one is how a project adds a type Aiko does not ship - an Epic,
        // a Bug, anything the project's own vocabulary needs.
        app.MapPost(
            "/api/v1/projects/{projectId}/workflows",
            async (
                string projectId,
                CreateWorkflowRequest request,
                IProjectDefinitionStore definitions,
                IUnifiedAgentInstaller agents,
                DaemonAccessToken accessToken,
                HttpRequest httpRequest,
                CancellationToken cancellationToken) =>
            {
                if (Validate(workflowId: request.Id, request.Title, request.Stages) is { } invalid)
                {
                    return invalid;
                }

                var created = new WorkflowDefinition(
                    request.Id.Trim().ToLowerInvariant(),
                    request.Title.Trim(),
                    request.Stages.OrderBy(stage => stage.Order).ToArray(),
                    1,
                    Normalize(request.Description),
                    AppearanceCatalog.NormalizeIcon(request.Icon),
                    AppearanceCatalog.NormalizeColor(request.Color));

                try
                {
                    await definitions.CreateWorkflowAsync(projectId, created, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new ErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }

                await ReprojectAsync(projectId, httpRequest, agents, accessToken, cancellationToken);
                return Results.Created(
                    $"/api/v1/projects/{projectId}/workflows/{created.Id}",
                    created);
            });

        app.MapPut(
            "/api/v1/projects/{projectId}/workflows/{workflowId}",
            async (
                string projectId,
                string workflowId,
                UpdateWorkflowRequest request,
                IProjectDefinitionStore definitions,
                ICardStore cards,
                IUnifiedAgentInstaller agents,
                DaemonAccessToken accessToken,
                HttpRequest httpRequest,
                CancellationToken cancellationToken) =>
            {
                if (Validate(workflowId, request.Title, request.Stages) is { } invalid)
                {
                    return invalid;
                }

                var definition = await definitions.ReadAsync(projectId, cancellationToken);
                var existing = definition.Workflows.FirstOrDefault(workflow =>
                    StringComparer.Ordinal.Equals(workflow.Id, workflowId));
                if (existing is null)
                {
                    return Results.NotFound();
                }
                if (existing.Revision != request.ExpectedRevision)
                {
                    return Results.Conflict(new RevisionConflictResponse(
                        request.ExpectedRevision,
                        existing.Revision));
                }

                var stageIds = request.Stages.Select(stage => stage.Id).ToHashSet(StringComparer.Ordinal);
                var orphanedCard = (await cards.ListAsync(projectId, cancellationToken)).FirstOrDefault(card =>
                    StringComparer.Ordinal.Equals(card.WorkflowId, workflowId) && !stageIds.Contains(card.StageId));
                if (orphanedCard is not null)
                {
                    return Results.BadRequest(new ErrorResponse(
                        $"Stage {orphanedCard.StageId} still contains card {orphanedCard.Reference.CardId}."));
                }

                var updated = new WorkflowDefinition(
                    existing.Id,
                    request.Title.Trim(),
                    request.Stages.OrderBy(stage => stage.Order).ToArray(),
                    existing.Revision + 1,
                    Normalize(request.Description),
                    AppearanceCatalog.NormalizeIcon(request.Icon),
                    AppearanceCatalog.NormalizeColor(request.Color));
                try
                {
                    await definitions.SaveWorkflowAsync(
                        projectId,
                        updated,
                        request.ExpectedRevision,
                        cancellationToken);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }

                await ReprojectAsync(projectId, httpRequest, agents, accessToken, cancellationToken);
                return Results.Ok(updated);
            });

        app.MapDelete(
            "/api/v1/projects/{projectId}/workflows/{workflowId}",
            async (
                string projectId,
                string workflowId,
                IProjectDefinitionStore definitions,
                ICardStore cards,
                IUnifiedAgentInstaller agents,
                DaemonAccessToken accessToken,
                HttpRequest httpRequest,
                CancellationToken cancellationToken) =>
            {
                var definition = await definitions.ReadAsync(projectId, cancellationToken);
                if (!definition.Workflows.Any(workflow =>
                        StringComparer.Ordinal.Equals(workflow.Id, workflowId)))
                {
                    return Results.NotFound();
                }

                // Removing the pipeline of a type that still has cards would strand them, exactly as removing
                // a stage would. The type has to be empty first.
                var stranded = (await cards.ListAsync(projectId, cancellationToken)).FirstOrDefault(card =>
                    StringComparer.Ordinal.Equals(card.WorkflowId, workflowId));
                if (stranded is not null)
                {
                    return Results.BadRequest(new ErrorResponse(
                        $"Card type still contains card {stranded.Reference.CardId}."));
                }

                try
                {
                    await definitions.DeleteWorkflowAsync(projectId, workflowId, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }

                await ReprojectAsync(projectId, httpRequest, agents, accessToken, cancellationToken);
                return Results.NoContent();
            });
    }

    /// <summary>
    /// Refreshes the per-type agent commands after the set of card types changed.
    /// </summary>
    /// <remarks>
    /// A failure here does not fail the request: the commands are a convenience derived from the pipelines,
    /// the workflow itself is already saved, and the next <c>aiko agent install</c> repairs the files.
    /// </remarks>
    private static async ValueTask ReprojectAsync(
        string projectId,
        HttpRequest httpRequest,
        IUnifiedAgentInstaller agents,
        DaemonAccessToken accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint =
                $"{httpRequest.Scheme}://{httpRequest.Host}/mcp/projects/{Uri.EscapeDataString(projectId)}";
            await agents.ReprojectCardTypesAsync(projectId, endpoint, accessToken.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// The checks a create and an update share: the id must be file-safe, the pipeline must be non-empty and
    /// it must keep the reserved backlog stage as its first step. Returns the failure result, or null when
    /// the request may proceed. Shared with the template endpoints, which apply the same rules.
    /// </summary>
    internal static IResult? Validate(
        string workflowId,
        string? title,
        IReadOnlyList<StageDefinition>? stages)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(title) ||
            stages is null || stages.Count == 0)
        {
            return Results.BadRequest(new ErrorResponse(
                "Workflow id, title and at least one stage are required."));
        }

        var backlog = stages.FirstOrDefault(WorkflowDefinition.IsBacklog);
        if (backlog is null)
        {
            return Results.BadRequest(new ErrorResponse(
                $"A workflow must keep its {WorkflowDefinition.BacklogStageId} stage."));
        }

        if (backlog.Order != stages.Min(stage => stage.Order))
        {
            return Results.BadRequest(new ErrorResponse(
                $"The {WorkflowDefinition.BacklogStageId} stage must be the first stage of the workflow."));
        }

        return null;
    }

    /// <summary>Trims an optional text field and turns the empty string into null.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
