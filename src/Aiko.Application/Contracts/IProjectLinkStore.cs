namespace Aiko.Application.Contracts;

/// <summary>
/// Store of the projects this one is linked to: <c>.aiko/links.json</c> is the source of truth.
/// </summary>
/// <remarks>
/// A registry of its own rather than a field on the project manifest: a link is written while the project runs,
/// it changes far more often than the manifest, and keeping the two apart means a broken link cannot stop a
/// project from being read.
/// </remarks>
public interface IProjectLinkStore
{
    /// <summary>
    /// Returns the links of a project; the project may be addressed by its id or by its readable handle.
    /// </summary>
    ValueTask<IReadOnlyList<ProjectLink>> ListAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or replaces the link to one project. The target is resolved through the registry of projects, so a
    /// project nobody has registered cannot be linked, and linking a project to itself is refused.
    /// </summary>
    /// <param name="projectId">Project whose registry is written.</param>
    /// <param name="targetProjectId">Id or handle of the project to link.</param>
    /// <param name="description">What that project is for, in the words of whoever linked it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProjectLink> SaveAsync(
        string projectId,
        string targetProjectId,
        string description,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the link to one project; a link that is not there is not an error.
    /// </summary>
    /// <param name="projectId">Project whose registry is written.</param>
    /// <param name="targetProjectId">Id or handle of the linked project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RemoveAsync(
        string projectId,
        string targetProjectId,
        CancellationToken cancellationToken);
}
