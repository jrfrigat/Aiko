namespace Aiko.Application.Agents;

/// <summary>Reads the agents the daemon knows and decides where a command goes.</summary>
public interface IAgentRegistry
{
    /// <summary>Lists every adapter with its effective configuration and current state.</summary>
    ValueTask<IReadOnlyList<RegisteredAgent>> ListAsync(
        string? projectId,
        CancellationToken cancellationToken);

    /// <summary>Chooses a direct agent or a queue route for a command.</summary>
    ValueTask<AgentRoute> ResolveAsync(
        string? projectId,
        AgentCommandRequest request,
        CancellationToken cancellationToken);
}
