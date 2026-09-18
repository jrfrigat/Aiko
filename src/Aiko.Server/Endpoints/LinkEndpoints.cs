using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Linked-project endpoints: the registry of projects this one hands work to, with what each of them is for.
/// </summary>
/// <remarks>
/// The links belong to the project, not to the daemon, so they are read and written through the project route
/// the rest of the UI uses. A link is addressed by the linked project's id or by its handle, because the person
/// writing it knows the handle and the file stores the id.
/// </remarks>
internal static class LinkEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects/{projectId}/links.
    /// </summary>
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/links",
            async (
                string projectId,
                IProjectLinkStore links,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await links.ListAsync(projectId, cancellationToken)));

        app.MapPut(
            "/api/v1/projects/{projectId}/links/{targetProjectId}",
            async (
                string projectId,
                string targetProjectId,
                LinkProjectRequest request,
                IProjectLinkStore links,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var link = await links.SaveAsync(
                        projectId,
                        targetProjectId,
                        request.Description,
                        cancellationToken);
                    return Results.Ok(link);
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new ErrorResponse(exception.Message));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
            });

        app.MapDelete(
            "/api/v1/projects/{projectId}/links/{targetProjectId}",
            async (
                string projectId,
                string targetProjectId,
                IProjectLinkStore links,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await links.RemoveAsync(projectId, targetProjectId, cancellationToken);
                    return Results.Ok(await links.ListAsync(projectId, cancellationToken));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new ErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
            });
    }
}
