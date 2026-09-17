using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Reads and saves the settings of a project. Effective values resolve per section: the project's own
/// document wins, and a section it does not state falls back to the built-in default.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Resolves the settings view for a project. A null project id answers with the built-in defaults, which
    /// is what a screen with no project open needs.
    /// </summary>
    ValueTask<AppSettingsView> LoadAsync(string? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the effective execution settings of a project.
    /// </summary>
    ValueTask<ExecutionSettings> GetEffectiveExecutionAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the effective priority settings of a project.
    /// </summary>
    ValueTask<PrioritySettings> GetEffectivePriorityAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the project-local settings.
    /// </summary>
    ValueTask SaveProjectAsync(
        string projectId,
        AppSettings settings,
        CancellationToken cancellationToken);
}
