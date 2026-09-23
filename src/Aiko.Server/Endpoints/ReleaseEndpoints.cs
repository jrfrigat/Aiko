using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// The release surfaces over HTTP: the facts of the last published release, and the history of what the
/// project released.
/// </summary>
/// <remarks>
/// Two different questions live here and stay apart. The facts (<c>GET /release</c>) are read-only and
/// assembled on demand from the repository and the tree, so a screen asking twice gets two probes rather
/// than a cached answer that may describe a tree that has moved on. The history (<c>GET /releases</c>) is a
/// document of the project, written by whoever records a release and read here: recording is the agent's
/// tool, so this surface has no writer.
/// </remarks>
internal static class ReleaseEndpoints
{
    /// <summary>
    /// Maps the release facts route and the three release-history routes.
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

        // The whole history, newest first, one line per release. It is read as a whole because it is drawn as a
        // whole, and because the store writes it newest first: the order a screen wants is the order the
        // document already holds, not a sort done here.
        app.MapGet(
            "/api/v1/projects/{projectId}/releases",
            async Task<IResult> (
                string projectId,
                IReleaseStore releases,
                CancellationToken cancellationToken) =>
            {
                var document = await releases.ReadAsync(projectId, cancellationToken);
                return Results.Ok(document.Entries
                    .Select(record => new ReleaseHistoryItem(
                        record.Version,
                        record.SchemeId,
                        record.ReleasedAt,
                        record.Cards.Count))
                    .ToArray());
            });

        // One release by version. An unknown version is a 404 and not an empty record: "there is no such
        // release" and "there is one and it carried nothing" are different answers.
        app.MapGet(
            "/api/v1/projects/{projectId}/releases/{version}",
            async Task<IResult> (
                string projectId,
                string version,
                IReleaseStore releases,
                ICardStore cards,
                CancellationToken cancellationToken) =>
            {
                var document = await releases.ReadAsync(projectId, cancellationToken);
                return document.Find(version) is { } record
                    ? Results.Ok(ReleaseView.From(
                        record,
                        await ReadTitlesAsync(projectId, record, cards, cancellationToken)))
                    : Results.NotFound(new ErrorResponse(
                        $"Release '{version}' is not recorded in this project."));
            });

        // Which release named this card: the newest one, because a card that went into a preliminary release
        // and then into the ordinary one belongs, to whoever reads the card, to the ordinary one - and every
        // record stays in the history, so answering with one of them loses nothing.
        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/release",
            async Task<IResult> (
                string projectId,
                string cardId,
                IReleaseStore releases,
                ICardStore cards,
                CancellationToken cancellationToken) =>
            {
                var document = await releases.ReadAsync(projectId, cancellationToken);
                // No release at all is a 404 rather than an empty object, which is what the explicit list
                // makes possible: a card is in a release because it was named, so the answer is either there
                // or not there. Whether the card itself exists is not asked - this route is about releases.
                return document.Entries.FirstOrDefault(record => record.Cards.Any(card =>
                        StringComparer.OrdinalIgnoreCase.Equals(card.Trim(), cardId.Trim()))) is { } record
                    ? Results.Ok(ReleaseView.From(
                        record,
                        await ReadTitlesAsync(projectId, record, cards, cancellationToken)))
                    : Results.NotFound(new ErrorResponse(
                        $"Card '{cardId}' is not named in any release of this project."));
            });
    }

    /// <summary>
    /// Reads the current title of every card a release named, keyed by the id as the record holds it.
    /// </summary>
    /// <remarks>
    /// The record is history and keeps ids only, so the titles come from the live cards. A card that is gone, an
    /// id that is not a valid card id, or a card file that cannot be read leaves that id without a title: the
    /// release page must open whatever became of the cards it lists. An archived card is still a card file, so
    /// it keeps its title.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, string>> ReadTitlesAsync(
        string projectId,
        ReleaseRecord record,
        ICardStore cards,
        CancellationToken cancellationToken)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cardId in record.Cards.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var card = await cards.FindAsync(new CardReference(projectId, cardId.Trim()), cancellationToken);
                if (card is { Title.Length: > 0 })
                {
                    titles[cardId] = card.Title;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or JsonException or KeyNotFoundException
                    or UnauthorizedAccessException)
            {
                // The id stays bare; see the remarks.
            }
        }

        return titles;
    }
}
