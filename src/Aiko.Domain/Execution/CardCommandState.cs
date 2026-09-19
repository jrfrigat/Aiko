using System.Text.Json.Serialization;

namespace Aiko.Domain.Execution;

/// <summary>
/// Where a command got to between the screen that placed it and the agent that carries it out.
/// </summary>
/// <remarks>
/// This state is the whole point of the queue: it is what a person reads as "waiting" or "taken", and it is
/// the only thing that keeps two agents from starting the same command twice. It therefore lives in the
/// command's own record rather than in the screen that drew it.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CardCommandState>))]
public enum CardCommandState
{
    /// <summary>Placed and waiting for an agent to take it.</summary>
    Queued,

    /// <summary>An agent has taken it and is carrying it out.</summary>
    Taken,

    /// <summary>The agent carried it out.</summary>
    Completed,

    /// <summary>The agent could not carry it out; the message says why.</summary>
    Failed,

    /// <summary>Nobody will carry it out - the person took it back before an agent claimed it.</summary>
    Cancelled
}
