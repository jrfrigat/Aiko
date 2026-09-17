using Aiko.Application.Contracts;
using Aiko.Domain.Cards;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Git endpoints: what the client on this machine says about a project, and the diff of one card's files.
/// </summary>
/// <remarks>
/// The daemon runs the user's own <c>git</c>; when there is none, the answer is a status that says so rather
/// than an error - a screen has to be able to draw "Git client unavailable".
/// </remarks>
internal static class GitEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects/{projectId}/git and /api/v1/projects/{projectId}/cards/{cardId}/diff.
    /// </summary>
    public static void MapGitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId}/git",
            async (
                string projectId,
                IProjectCatalog catalog,
                IGitClient git,
                CancellationToken cancellationToken) =>
            {
                var project = await catalog.FindAsync(projectId, cancellationToken);
                if (project is null)
                {
                    return Results.NotFound();
                }

                var status = await git.StatusAsync(project.RootPath, cancellationToken);
                var commits = status.IsRepository
                    ? await git.LogAsync(project.RootPath, 10, cancellationToken)
                    : [];
                return Results.Ok(new GitOverview(status, commits));
            });

        app.MapGet(
            "/api/v1/projects/{projectId}/cards/{cardId}/diff",
            async (
                string projectId,
                string cardId,
                IProjectCatalog catalog,
                ICardStore cards,
                IGitClient git,
                CancellationToken cancellationToken) =>
            {
                var project = await catalog.FindAsync(projectId, cancellationToken);
                if (project is null)
                {
                    return Results.NotFound();
                }

                var card = await cards.FindAsync(new CardReference(projectId, cardId), cancellationToken);
                if (card is null)
                {
                    return Results.NotFound();
                }

                // What the card actually touched comes first, because that is what a reader compares against
                // the declared scope; a card with no run yet falls back to what it declared.
                var paths = card.ActualChangedFiles.Count > 0
                    ? card.ActualChangedFiles
                    : card.DeclaredScopeFiles;
                return Results.Ok(await git.DiffAsync(
                    project.RootPath,
                    paths.Select(Normalize).ToArray(),
                    cancellationToken));
            });
    }

    /// <summary>
    /// A card's path as a pathspec: git speaks forward slashes and reads a leading slash as the repository
    /// root, while a project's scope is written either way.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
