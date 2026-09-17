using Aiko.Application.Contracts;
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
