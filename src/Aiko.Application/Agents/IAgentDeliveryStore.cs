using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>Persistence of the direct-delivery attempts of a project.</summary>
public interface IAgentDeliveryStore
{
    /// <summary>Reads the project's attempts, oldest first.</summary>
    ValueTask<IReadOnlyList<AgentDelivery>> ListAsync(
        string projectId,
        bool includeClosed,
        CancellationToken cancellationToken);

    /// <summary>Finds one attempt or returns null.</summary>
    ValueTask<AgentDelivery?> FindAsync(
        string projectId,
        string deliveryId,
        CancellationToken cancellationToken);

    /// <summary>Finds the newest attempt for a command, or null.</summary>
    ValueTask<AgentDelivery?> FindLatestForCommandAsync(
        string projectId,
        string commandId,
        CancellationToken cancellationToken);

    /// <summary>Creates a Starting attempt and assigns it an identifier.</summary>
    ValueTask<AgentDelivery> CreateAsync(
        string projectId,
        string commandId,
        string agentAdapterId,
        AgentTransport transport,
        CancellationToken cancellationToken);

    /// <summary>Replaces an attempt with its new state.</summary>
    ValueTask<AgentDelivery> UpdateAsync(
        string projectId,
        AgentDelivery delivery,
        CancellationToken cancellationToken);
}
