using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Infrastructure.Settings;

/// <summary>
/// Merges global and project-local settings snapshots into effective values:
/// per section, project-local settings win over global settings,
/// which win over the built-in safe defaults.
/// </summary>
public sealed class AppSettingsService(IAppSettingsStore store) : IAppSettingsService
{
    /// <inheritdoc />
    public async ValueTask<AppSettingsView> LoadAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        var global = await store.ReadGlobalAsync(cancellationToken);
        var project = projectId is null
            ? null
            : await store.ReadProjectAsync(projectId, cancellationToken);

        var execution = ExecutionSettings.SafeDefault;
        var executionSource = AppSettingsSources.Default;
        if (global?.Execution is not null)
        {
            execution = global.Execution;
            executionSource = AppSettingsSources.Global;
        }

        if (project?.Execution is not null)
        {
            execution = project.Execution;
            executionSource = AppSettingsSources.Project;
        }

        var priority = PrioritySettings.SafeDefault;
        var prioritySource = AppSettingsSources.Default;
        if (global?.Priority is not null)
        {
            priority = global.Priority;
            prioritySource = AppSettingsSources.Global;
        }

        if (project?.Priority is not null)
        {
            priority = project.Priority;
            prioritySource = AppSettingsSources.Project;
        }

        return new AppSettingsView(
            global,
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
    public ValueTask SaveGlobalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return store.SaveGlobalAsync(
            settings with { SchemaVersion = AppSettings.CurrentSchemaVersion },
            cancellationToken);
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
