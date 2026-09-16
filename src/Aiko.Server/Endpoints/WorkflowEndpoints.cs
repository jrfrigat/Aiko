using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Workflow endpoints: updating workflow definitions with optimistic revisions.
/// </summary>
internal static class WorkflowEndpoints
{
    /// <summary>
    /// Maps PUT /api/v1/projects/{projectId}/workflows/{workflowId}.
    /// </summary>
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(
            "/api/v1/projects/{projectId}/workflows/{workflowId}",
            async (
                string projectId,
                string workflowId,
                UpdateWorkflowRequest request,
                IProjectDefinitionStore definitions,
                ICardStore cards,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title) || request.Stages is null || request.Stages.Count == 0)
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Workflow title and at least one stage are required."));
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
                    existing.Revision + 1);
                await definitions.SaveWorkflowAsync(
                    projectId,
                    updated,
                    request.ExpectedRevision,
                    cancellationToken);

                return Results.Ok(updated);
            });
    }
}
