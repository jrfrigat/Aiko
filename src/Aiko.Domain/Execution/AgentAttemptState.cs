using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// State of a single agent attempt within a stage execution.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentAttemptState>))]
public enum AgentAttemptState
{
    /// <summary>The attempt is planned but has not started yet.</summary>
    Queued,

    /// <summary>The agent is doing the work.</summary>
    Running,

    /// <summary>The agent is waiting for a user decision.</summary>
    WaitingForUser,

    /// <summary>The agent hit its rate limit; hand the attempt to another agent.</summary>
    RateLimited,

    /// <summary>The attempt is paused and can be resumed.</summary>
    Paused,

    /// <summary>The attempt failed.</summary>
    Failed,

    /// <summary>The attempt was cancelled by the user.</summary>
    Cancelled,

    /// <summary>The attempt was replaced by another one (handoff) before finishing.</summary>
    Superseded,

    /// <summary>The attempt completed successfully.</summary>
    Completed
}
