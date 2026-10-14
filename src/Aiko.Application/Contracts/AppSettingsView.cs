using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Well-known sources of an effective settings value.
/// </summary>
public static class AppSettingsSources
{
    /// <summary>The value comes from the project's own settings (<c>.aiko/settings.json</c>).</summary>
    public const string Project = "project";

    /// <summary>No explicit configuration: the built-in safe default is used.</summary>
    public const string Default = "default";
}

/// <summary>
/// Resolved settings for one level: the document the level states (null when it states nothing) plus the
/// effective execution settings and priority model, with the source that provided each - <c>project</c> or
/// <c>default</c>.
/// </summary>
public sealed record AppSettingsView(
    AppSettings? Snapshot,
    ExecutionSettings EffectiveExecution,
    string ExecutionSource,
    PrioritySettings EffectivePriority,
    string PrioritySource);
