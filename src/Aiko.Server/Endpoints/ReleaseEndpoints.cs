using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The release facts over HTTP: what the release screen needs and nothing it could derive elsewhere.
/// </summary>
/// <remarks>
/// One route and one provider. The facts are read-only and assembled on demand, so a screen asking twice gets
/// two probes rather than a cached answer that may describe a tree that has moved on.
/// </remarks>
internal static class ReleaseEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects/{projectId}/release.
    /// </summary>
    public static void MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/release",
            async Task<IResult> (
                string projectId,
                IReleaseInfoProvider release,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await release.ReadAsync(projectId, cancellationToken));
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
