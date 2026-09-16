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
        app.MapPost(
            "/api/v1/projects/{projectId}/reindex",
            async (
                string projectId,
                IProjectReindexer reindexer,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await reindexer.ReindexAsync(projectId, cancellationToken)));
    }
}
