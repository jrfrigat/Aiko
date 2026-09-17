using Aiko.Application.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Filesystem endpoints: the directory browser behind the UI's project picker.
/// </summary>
/// <remarks>
/// Directories only, never file names or contents, and loopback-only like the rest of the API: this is
/// the user's own machine asking about itself, not a remote file service. The route is deliberately
/// absent from MCP - an agent has its own project-scoped tools and no business inventorying the disk.
/// </remarks>
internal static class FileSystemEndpoints
{
    /// <summary>
    /// Maps /api/v1/fs/directories.
    /// </summary>
    public static void MapFileSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/fs/directories",
            async (string? path, IDirectoryBrowser browser, CancellationToken cancellationToken) =>
                TypedResults.Ok(await browser.ListAsync(path, cancellationToken)));
    }
}
