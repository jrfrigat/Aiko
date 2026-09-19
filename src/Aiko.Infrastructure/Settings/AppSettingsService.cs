using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// Resolves the settings a project runs with: its own document, or the built-in safe defaults for a
/// section it does not state. There is no level above the project - what a project does not state it does
/// not inherit from anything, because a project is created as a copy of a template and owns it from then
/// on.
/// </summary>
public sealed class AppSettingsService(IAppSettingsStore store) : IAppSettingsService
{
    /// <inheritdoc />
    public async ValueTask<AppSettingsView> LoadAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        var project = projectId is null
            ? null
            : await store.ReadProjectAsync(projectId, cancellationToken);

        var execution = project?.Execution ?? ExecutionSettings.SafeDefault;
        var executionSource = project?.Execution is null
            ? AppSettingsSources.Default
            : AppSettingsSources.Project;

        var priority = project?.Priority ?? PrioritySettings.SafeDefault;
        var prioritySource = project?.Priority is null
            ? AppSettingsSources.Default
            : AppSettingsSources.Project;

        return new AppSettingsView(
            project,
            execution,
            executionSource,
            priority,
            prioritySource);
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionSettings> GetEffectiveExecutionAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var view = await LoadAsync(projectId, cancellationToken);
        return view.EffectiveExecution;
    }

    /// <inheritdoc />
    public async ValueTask<PrioritySettings> GetEffectivePriorityAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var view = await LoadAsync(projectId, cancellationToken);
        return view.EffectivePriority;
    }

    /// <inheritdoc />
    public async ValueTask<CrossProjectSettings> GetEffectiveCrossProjectAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        // Read through the store rather than the view: the policy is a single section, and a project that does
        // not state one gets the safe default, which is what "silence means no" reads as.
        var project = await store.ReadProjectAsync(projectId, cancellationToken);
        return project?.CrossProject ?? CrossProjectSettings.SafeDefault;
    }

    /// <inheritdoc />
    public ValueTask SaveProjectAsync(
        string projectId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(settings);
        return store.SaveProjectAsync(
            projectId,
            settings with { SchemaVersion = AppSettings.CurrentSchemaVersion },
            cancellationToken);
    }
}
