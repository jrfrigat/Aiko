using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Project endpoints: listing registered projects and initializing new ones.
/// </summary>
internal static class ProjectEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects, /api/v1/projects/initialize and /api/v1/projects/{projectId}/reindex.
    /// </summary>
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects",
            async (IProjectCatalog catalog, CancellationToken cancellationToken) =>
                TypedResults.Ok(await catalog.ListAsync(cancellationToken)));
        app.MapPost(
            "/api/v1/projects/initialize",
            async (
                InitializeProjectRequest request,
                IProjectInitializer initializer,
                CancellationToken cancellationToken) =>
            {
                var project = await initializer.InitializeAsync(request, cancellationToken);
                return TypedResults.Created($"/api/v1/projects/{project.Id}", project);
            });
        // The templates a project can be created from. Read-only: the only one that exists today is the
        // built-in default, and authoring templates is the post-MVP half of the feature.
        app.MapGet(
            "/api/v1/templates",
            async (IProjectTemplateStore templates, CancellationToken cancellationToken) =>
                TypedResults.Ok(await templates.ListAsync(cancellationToken)));
        // One template in full: its settings and its pipelines. The defaults screen reads this, edits one
        // slice of it and writes that slice back, so the two writers never clobber each other.
        app.MapGet(
            "/api/v1/templates/{templateId}",
            async (
                string templateId,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
            });
        app.MapPut(
            "/api/v1/templates/{templateId}/settings",
            async (
                string templateId,
                AppSettings request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                // One version for the whole document: the settings are part of the template's content, and
                // projects record which version of it they were created from.
                await templates.WriteAsync(
                    template with { Settings = request, Version = template.Version + 1 },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });
        app.MapPut(
            "/api/v1/templates/{templateId}/workflows/{workflowId}",
            async (
                string templateId,
                string workflowId,
                UpdateWorkflowRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title) || request.Stages is null || request.Stages.Count == 0)
                {
                    return Results.BadRequest(new ErrorResponse(
                        "Workflow title and at least one stage are required."));
                }

                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                var existing = template.Workflows.FirstOrDefault(workflow =>
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

                var updated = new WorkflowDefinition(
                    existing.Id,
                    request.Title.Trim(),
                    request.Stages.OrderBy(stage => stage.Order).ToArray(),
                    existing.Revision + 1);
                // A template has no cards, so the "stage still contains cards" rule of the project endpoint
                // has nothing to check here: a template's pipeline is a starting point, not a live board.
                var workflows = template.Workflows
                    .Select(workflow => StringComparer.Ordinal.Equals(workflow.Id, workflowId) ? updated : workflow)
                    .ToArray();

                await templates.WriteAsync(
                    template with { Workflows = workflows, Version = template.Version + 1 },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });
        app.MapPost(
            "/api/v1/projects/{projectId}/reindex",
            async (
                string projectId,
                IProjectReindexer reindexer,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await reindexer.ReindexAsync(projectId, cancellationToken)));
        // Unregistering, not deleting: the registration and its SQLite projections go, the project's
        // files stay. A mistyped `aiko init` path has to be undoable, so this cannot be a destructive
        // operation by default.
        app.MapDelete(
            "/api/v1/projects/{projectId}",
            async Task<IResult> (
                string projectId,
                IProjectCatalog catalog,
                CancellationToken cancellationToken) =>
            {
                var removed = await catalog.RemoveAsync(projectId, cancellationToken);
                return removed ? TypedResults.NoContent() : TypedResults.NotFound();
            });
    }
}
