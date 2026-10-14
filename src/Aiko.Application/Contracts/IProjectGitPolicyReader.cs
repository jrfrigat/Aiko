namespace Aiko.Application.Contracts;

/// <summary>
/// Reads the git policy a project was initialized with, so a caller can tell an agent how this project's
/// repository treats Aiko's own data. The policy lives in the project's manifest and not in the catalog, so a
/// project registered from a path carries it even after the database was rebuilt - which is why the reader is
/// separate from <see cref="IProjectCatalog"/>, and why an unreadable manifest answers null rather than a
/// guessed default.
/// </summary>
public interface IProjectGitPolicyReader
{
    /// <summary>The policy recorded in the project's own manifest, or null when it has no readable manifest -
    /// one registered before manifests existed, or a path that moved.</summary>
    ValueTask<ProjectGitPolicy?> ReadAsync(
        RegisteredProject project,
        CancellationToken cancellationToken);
}
