namespace Aiko.Application.Agents;

/// <summary>Starts an agent through a concrete SDK and runs one delivery.</summary>
public interface IAgentSdkAdapter
{
    /// <summary>Identifier recorded in <see cref="AgentSdkConfig.Adapter"/>.</summary>
    string Id { get; }

    /// <summary>Runs the delivery and reports its outcome.</summary>
    ValueTask<AgentTransportResult> DeliverAsync(
        AgentDeliveryContext context,
        CancellationToken cancellationToken);
}
