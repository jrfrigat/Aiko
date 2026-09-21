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
    /// Returns the effective cross-project write policy of a project: whether an agent in another project may
    /// create cards here, and which projects may.
    /// </summary>
    /// <param name="projectId">Project the policy belongs to, which is the project sending the work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CrossProjectSettings> GetEffectiveCrossProjectAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the effective release section of a project: the GitHub repository its releases are published
    /// in, or the safe default when it states none.
    /// </summary>
    /// <param name="projectId">Project to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ReleaseSettings> GetEffectiveReleaseAsync(
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
