using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// Workspace mode in which a stage is executed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceMode>))]
public enum WorkspaceMode
{
    /// <summary>
    /// Work directly in the shared project checkout.
    /// </summary>
    Shared,

    /// <summary>
    /// Work in a dedicated git worktree.
    /// </summary>
    Worktree
}
