namespace Aiko.Application.Contracts;

/// <summary>
/// Catalog of projects known to the Aiko daemon.
/// </summary>
public interface IProjectCatalog
{
    /// <summary>
    /// Returns all registered projects.
    /// </summary>
    ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds a project by its stable id or by its human-readable slug, or returns null when neither matches.
    /// </summary>
    /// <remarks>
    /// Accepting both is what lets links and agent MCP endpoints carry the readable handle while every card
    /// file and relation keeps the immutable id. The id is tried first, so nothing that resolved before
    /// stops resolving, and a project without a slug is still reachable by id.
    /// </remarks>
    ValueTask<RegisteredProject?> FindAsync(string projectIdOrSlug, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a slug already belongs to a project, ignoring the one named by
    /// <paramref name="exceptProjectId"/>.
    /// </summary>
    /// <remarks>
    /// The check a create performs before writing anything: a slug the user typed must be reported as taken
    /// rather than silently changed, which is the opposite of what a derived slug does.
    /// </remarks>
    /// <param name="slug">Slug to test.</param>
    /// <param name="exceptProjectId">Project allowed to keep it, or null when none is.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<bool> IsSlugTakenAsync(
        string slug,
        string? exceptProjectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a project registration.
    /// </summary>
    ValueTask SaveAsync(RegisteredProject project, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a project registration, along with the SQLite projections that hang off it (cards,
    /// executions and events cascade). The project's files on disk are never touched: unregistering is
    /// not deleting, and a mistyped path has to be recoverable.
    /// </summary>
    /// <returns>True when a registration was actually removed.</returns>
    ValueTask<bool> RemoveAsync(string projectId, CancellationToken cancellationToken);
}
