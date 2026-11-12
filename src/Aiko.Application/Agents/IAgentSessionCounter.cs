namespace Aiko.Application.Agents;

/// <summary>Counts the direct sessions an agent currently runs.</summary>
public interface IAgentSessionCounter
{
    /// <summary>Number of active direct sessions for the agent instance.</summary>
    ValueTask<int> CountAsync(string instanceId, CancellationToken cancellationToken);
}
