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
}
