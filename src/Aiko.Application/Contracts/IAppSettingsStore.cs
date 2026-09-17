namespace Aiko.Application.Contracts;

/// <summary>
/// Persistence of a project's settings: the .aiko/settings.json it owns. There is no installation-level
/// document: a project is created from a template and owns its copy from then on.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>
    /// Reads the project-local settings or returns null when the project has none
    /// (every section then falls back to the built-in safe defaults).
    /// </summary>
    ValueTask<AppSettings?> ReadProjectAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically saves the project-local settings.
    /// </summary>
    ValueTask SaveProjectAsync(string projectId, AppSettings settings, CancellationToken cancellationToken);
}
