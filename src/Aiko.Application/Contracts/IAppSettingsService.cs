using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Reads, merges and saves application settings. Effective values resolve per section:
/// project-local settings win over global settings, which win over built-in defaults.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Resolves the settings view for a project; pass null to inspect the global scope only.
    /// </summary>
    ValueTask<AppSettingsView> LoadAsync(string? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the effective execution settings of a project (project -> global -> safe default).
    /// </summary>
    ValueTask<ExecutionSettings> GetEffectiveExecutionAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the effective priority settings of a project (project -> global -> default).
    /// </summary>
    ValueTask<PrioritySettings> GetEffectivePriorityAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the global application settings.
    /// </summary>
    ValueTask SaveGlobalAsync(AppSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the project-local settings.
    /// </summary>
    ValueTask SaveProjectAsync(
        string projectId,
        AppSettings settings,
        CancellationToken cancellationToken);
}
