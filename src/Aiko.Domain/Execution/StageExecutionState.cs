using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// State of a card stage execution in the pipeline.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StageExecutionState>))]
public enum StageExecutionState
{
    /// <summary>The stage is being executed by an agent.</summary>
    Running,

    /// <summary>The execution is suspended, waiting for a user decision.</summary>
    WaitingForUser,

    /// <summary>The execution is explicitly paused.</summary>
    Paused,

    /// <summary>The execution needs attention: agent failure or rate limit.</summary>
    NeedsAttention,

    /// <summary>The stage completed successfully.</summary>
    Completed,

    /// <summary>The execution was cancelled.</summary>
    Cancelled
}
