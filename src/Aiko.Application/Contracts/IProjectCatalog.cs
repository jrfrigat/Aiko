namespace Aiko.Application.Contracts;

/// <summary>
/// Catalog of projects known to the Aiko daemon.
/// </summary>
public interface IProjectCatalog
{
    /// <summary>Returns all registered projects.</summary>
    ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds a project by its stable id or by its human-readable slug, or returns null when neither matches.
    /// The id is tried first, so nothing that resolved before stops resolving, and accepting both is what lets
    /// links and MCP endpoints carry the readable handle while every card file keeps the immutable id.
    /// </summary>
    ValueTask<RegisteredProject?> FindAsync(string projectIdOrSlug, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a slug already belongs to a project, ignoring the one named by
    /// <paramref name="exceptProjectId"/>. This is the check a create performs before writing anything: a slug
    /// the user typed must be reported as taken rather than silently changed.
    /// </summary>
    ValueTask<bool> IsSlugTakenAsync(
        string slug,
        string? exceptProjectId,
        CancellationToken cancellationToken);

    /// <summary>Creates or updates a project registration.</summary>
    ValueTask SaveAsync(RegisteredProject project, CancellationToken cancellationToken);

    /// <summary>Removes a project registration, with the SQLite projections that hang off it (cards,
    /// executions and events cascade). The files on disk are never touched: unregistering is not deleting.</summary>
    /// <returns>True when a registration was actually removed.</returns>
    ValueTask<bool> RemoveAsync(string projectId, CancellationToken cancellationToken);
}
