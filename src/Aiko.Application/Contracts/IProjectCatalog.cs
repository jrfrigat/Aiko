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
    /// Finds a project by identifier or returns null.
    /// </summary>
    ValueTask<RegisteredProject?> FindAsync(string projectId, CancellationToken cancellationToken);

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
