namespace Aiko.Application.Agents;

/// <summary>Checks whether an agent's configured transport can be used right now.</summary>
public interface IAgentTransportHealth
{
    /// <summary>True when the agent can be reached over its configured transport.</summary>
    ValueTask<bool> IsAvailableAsync(
        EffectiveAgentConfig config,
        CancellationToken cancellationToken);
}
