using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Well-known sources of an effective settings value, from most to least specific.
/// </summary>
public static class AppSettingsSources
{
    /// <summary>
    /// The value comes from project-local settings (.aiko/settings.json).
    /// </summary>
    public const string Project = "project";

    /// <summary>
    /// The value comes from global application settings (app-settings.json next to the database).
    /// </summary>
    public const string Global = "global";

    /// <summary>
    /// No explicit configuration: the built-in safe default is used.
    /// </summary>
    public const string Default = "default";
}

/// <summary>
/// Resolved settings for a scope: the raw global and project snapshots plus the effective
/// execution settings and priority weights with the name of the level that provided them.
/// </summary>
public sealed record AppSettingsView(
    AppSettings? Global,
    AppSettings? Project,
    ExecutionSettings EffectiveExecution,
    string ExecutionSource,
    PrioritySettings EffectivePriority,
    string PrioritySource);
