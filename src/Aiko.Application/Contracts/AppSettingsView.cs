using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Well-known sources of an effective settings value.
/// </summary>
public static class AppSettingsSources
{
    /// <summary>
    /// The value comes from the project's own settings (.aiko/settings.json).
    /// </summary>
    public const string Project = "project";

    /// <summary>
    /// No explicit configuration: the built-in safe default is used.
    /// </summary>
    public const string Default = "default";
}

/// <summary>
/// Resolved settings for one level: the document the level states (null when it states nothing) plus the
/// effective execution settings and priority model with the source that provided each.
/// </summary>
/// <param name="Snapshot">The level's own document, or null when it has none and the defaults apply.</param>
/// <param name="EffectiveExecution">The execution settings in force.</param>
/// <param name="ExecutionSource">Where those came from: the project, or the built-in defaults.</param>
/// <param name="EffectivePriority">The priority model in force.</param>
/// <param name="PrioritySource">Where that came from: the project, or the built-in defaults.</param>
public sealed record AppSettingsView(
    AppSettings? Snapshot,
    ExecutionSettings EffectiveExecution,
    string ExecutionSource,
    PrioritySettings EffectivePriority,
    string PrioritySource);
