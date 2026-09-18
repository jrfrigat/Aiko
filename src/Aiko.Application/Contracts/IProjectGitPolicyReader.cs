namespace Aiko.Application.Contracts;

/// <summary>
/// Reads the git policy a project was initialized with, so a caller can tell an agent how this project's
/// repository treats Aiko's own data.
/// </summary>
/// <remarks>
/// The policy lives in the project's manifest, not in the catalog: it is a property of the project's own
/// files, and a project registered from a path carries it even when the database was rebuilt. That is why the
/// reader is separate from <see cref="IProjectCatalog"/>, and why an unreadable manifest answers null rather
/// than a guessed default.
/// </remarks>
public interface IProjectGitPolicyReader
{
    /// <summary>
    /// The git policy recorded in the project's manifest, or null when the project has no readable manifest -
    /// one registered before manifests existed, or a path that moved.
    /// </summary>
    ValueTask<ProjectGitPolicy?> ReadAsync(
        RegisteredProject project,
        CancellationToken cancellationToken);
}
