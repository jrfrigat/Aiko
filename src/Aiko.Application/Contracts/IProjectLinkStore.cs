namespace Aiko.Application.Contracts;

/// <summary>
/// Store of the projects this one is linked to: <c>.aiko/links.json</c> is the source of truth. A registry of
/// its own rather than a field on the project manifest - a link is written while the project runs and changes
/// far more often than the manifest, and keeping the two apart means a broken link cannot stop a project from
/// being read.
/// </summary>
public interface IProjectLinkStore
{
    /// <summary>Returns the links of a project, addressed by its id or by its readable handle.</summary>
    ValueTask<IReadOnlyList<ProjectLink>> ListAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds or replaces the link to one project, resolving the target through the registry of projects: a
    /// project nobody registered cannot be linked, and linking a project to itself is refused. The entry is
    /// replaced whole, so a text left out is cleared - blank and absent mean the same thing on the way in.
    /// </summary>
    ValueTask<ProjectLink> SaveAsync(
        string projectId,
        string targetProjectId,
        ProjectLinkText text,
        CancellationToken cancellationToken);

    /// <summary>Removes the link to one project; a link that is not there is not an error.</summary>
    ValueTask RemoveAsync(
        string projectId,
        string targetProjectId,
        CancellationToken cancellationToken);
}
