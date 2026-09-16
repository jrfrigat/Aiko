using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Card artifact endpoints: listing, reading and saving Markdown artifacts.
/// </summary>
internal static class ArtifactEndpoints
{
    /// <summary>
    /// Maps the artifact listing, reading and saving routes.
    /// </summary>
    public static void MapArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/artifacts",
            async (
                string projectId,
                string cardId,
                ICardArtifactStore artifacts,
                CancellationToken cancellationToken) =>
            {
                return Results.Ok(await artifacts.ListAsync(
                    new CardReference(projectId, cardId),
                    cancellationToken));
            });
        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/artifact",
            async (
                string projectId,
                string cardId,
                string path,
                ICardArtifactStore artifacts,
                CancellationToken cancellationToken) =>
            {
                var document = await artifacts.ReadAsync(
                    new CardReference(projectId, cardId),
                    path,
                    cancellationToken);
                return document is null ? Results.NotFound() : Results.Ok(document);
            });
        app.MapPut(
            "/api/v1/projects/{projectId}/cards/{cardId}/artifact",
            async (
                string projectId,
                string cardId,
                UpdateArtifactRequest request,
                ICardArtifactStore artifacts,
                CancellationToken cancellationToken) =>
            {
                return Results.Ok(await artifacts.SaveAsync(
                    new CardReference(projectId, cardId),
                    request.Path,
                    request.Content,
                    request.ExpectedVersion,
                    cancellationToken));
            });
    }
}
