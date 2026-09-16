namespace Aiko.Application.Contracts;

/// <summary>
/// Persistence of application settings at both levels: the global file next to the
/// daemon database and the project-local .aiko/settings.json.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>
    /// Reads the global application settings or returns null when the file does not exist.
    /// </summary>
    ValueTask<AppSettings?> ReadGlobalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically saves the global application settings.
    /// </summary>
    ValueTask SaveGlobalAsync(AppSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the project-local settings or returns null when the project has none
    /// (every section then inherits from the global level).
    /// </summary>
    ValueTask<AppSettings?> ReadProjectAsync(string projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically saves the project-local settings.
    /// </summary>
    ValueTask SaveProjectAsync(string projectId, AppSettings settings, CancellationToken cancellationToken);
}
