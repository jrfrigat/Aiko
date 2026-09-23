using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The project memory over HTTP: listing, searching, reading, writing and removing the Markdown documents under
/// <c>.aiko/memory</c> - the same store the MCP tools use, so a screen and an agent see one memory.
/// </summary>
/// <remarks>
/// The project in the route may be its readable handle. It is resolved to the immutable id before the store is
/// asked, because the search index is keyed by the id: a document written under the handle has to be found under
/// either address. Paths go through the store's own resolution, so nothing outside the memory directory and
/// nothing but Markdown is ever reached; a refused path is a 400 with its reason, an unknown project or a missing
/// document a 404.
/// </remarks>
internal static class MemoryEndpoints
{
    /// <summary>The most documents one search returns, the same bound the MCP tool keeps.</summary>
    private const int MaximumSearchResults = 100;

    /// <summary>
    /// Maps the memory routes.
    /// </summary>
    public static void MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/memory",
            async (string projectId, IProjectCatalog projects, IMemoryStore memory, CancellationToken cancellationToken) =>
                Results.Ok(await memory.ListAsync(
                    await ResolveProjectIdAsync(projects, projectId, cancellationToken),
                    cancellationToken)));

        app.MapGet(
            "/api/v1/projects/{projectId}/memory/search",
            async (
                string projectId,
                string? q,
                int? limit,
                IProjectCatalog projects,
                IMemoryStore memory,
                CancellationToken cancellationToken) =>
            {
                var id = await ResolveProjectIdAsync(projects, projectId, cancellationToken);
                if (string.IsNullOrWhiteSpace(q))
                {
                    return Results.BadRequest(new ErrorResponse("A search needs the text to look for in 'q'."));
                }

                return Results.Ok(await memory.SearchAsync(
                    id,
                    q,
                    Math.Clamp(limit ?? 20, 1, MaximumSearchResults),
                    cancellationToken));
            });

        app.MapGet(
            "/api/v1/projects/{projectId}/memory/document",
            async (
                string projectId,
                string path,
                IProjectCatalog projects,
                IMemoryStore memory,
                CancellationToken cancellationToken) =>
            {
                var id = await ResolveProjectIdAsync(projects, projectId, cancellationToken);
                return await memory.ReadAsync(id, path, cancellationToken) is { } document
                    ? Results.Ok(document)
                    : Results.NotFound(new ErrorResponse($"No memory document '{path}' in this project."));
            });

        app.MapPut(
            "/api/v1/projects/{projectId}/memory/document",
            async (
                string projectId,
                StoreMemoryRequest request,
                IProjectCatalog projects,
                IMemoryStore memory,
                CancellationToken cancellationToken) =>
            {
                var id = await ResolveProjectIdAsync(projects, projectId, cancellationToken);
                await memory.StoreAsync(id, request.Path, request.Content, cancellationToken);
                return Results.Ok(await memory.ReadAsync(id, request.Path, cancellationToken));
            });

        app.MapDelete(
            "/api/v1/projects/{projectId}/memory/document",
            async (
                string projectId,
                string path,
                IProjectCatalog projects,
                IMemoryStore memory,
                CancellationToken cancellationToken) =>
            {
                var id = await ResolveProjectIdAsync(projects, projectId, cancellationToken);
                await memory.RemoveAsync(id, path, cancellationToken);
                return Results.NoContent();
            });
    }

    private static async Task<string> ResolveProjectIdAsync(
        IProjectCatalog projects,
        string projectId,
        CancellationToken cancellationToken) =>
        (await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}")).Id;
}
