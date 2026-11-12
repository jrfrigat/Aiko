using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>Delivers a command to an agent without waiting for a claim.</summary>
public interface IAgentTransport
{
    /// <summary>Transport this implementation handles.</summary>
    AgentTransport Transport { get; }

    /// <summary>Runs the delivery. Returning is the end of the attempt.</summary>
    ValueTask<AgentTransportResult> DeliverAsync(
        AgentDeliveryContext context,
        CancellationToken cancellationToken);
}

/// <summary>Everything a transport needs for one delivery attempt.</summary>
/// <param name="ProjectId">Project holding the command.</param>
/// <param name="Command">Command being delivered.</param>
/// <param name="Delivery">Attempt record.</param>
/// <param name="Agent">Agent configuration the route chose.</param>
public sealed record AgentDeliveryContext(
    string ProjectId,
    CardCommand Command,
    AgentDelivery Delivery,
    EffectiveAgentConfig Agent);

/// <summary>What a transport reports back.</summary>
/// <param name="Succeeded">Whether the delivery finished normally.</param>
/// <param name="SessionId">Transport session, when one was opened.</param>
/// <param name="Message">What the agent or the transport reported.</param>
public sealed record AgentTransportResult(
    bool Succeeded,
    string? SessionId = null,
    string? Message = null);
