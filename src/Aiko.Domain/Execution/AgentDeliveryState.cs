using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>Where one direct delivery attempt got to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentDeliveryState>))]
public enum AgentDeliveryState
{
    /// <summary>Created; the transport is about to start.</summary>
    Starting,

    /// <summary>The transport accepted the command and the agent is working.</summary>
    Running,

    /// <summary>The delivery finished normally.</summary>
    Completed,

    /// <summary>The transport or the agent failed; the command went back to the queue.</summary>
    Failed,

    /// <summary>A person cancelled the delivery.</summary>
    Cancelled,

    /// <summary>The daemon stopped while the delivery was in flight.</summary>
    Interrupted,

    /// <summary>The session is gone and cannot be resumed.</summary>
    Lost
}
