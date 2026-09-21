using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;

namespace Aiko.Application.Contracts;

/// <summary>
/// Settings snapshot of one level (global application settings or project-local settings).
/// Sections are optional: a missing section inherits from the less specific level,
/// so new sections can be added later without changing the storage format.
/// </summary>
public sealed record AppSettings(
    int SchemaVersion,
    ExecutionSettings? Execution = null,
    PrioritySettings? Priority = null,
    CrossProjectSettings? CrossProject = null,
    ReleaseSettings? Release = null)
{
    /// <summary>
    /// Schema version written by the current application version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}
