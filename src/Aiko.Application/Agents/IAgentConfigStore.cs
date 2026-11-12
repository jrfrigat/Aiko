namespace Aiko.Application.Agents;

/// <summary>Persistence of the user-level and project-level agent configuration files.</summary>
public interface IAgentConfigStore
{
    /// <summary>Reads the user-level document, or null when the agent has none.</summary>
    ValueTask<AgentConfig?> ReadUserAsync(string adapterId, CancellationToken cancellationToken);

    /// <summary>Reads the project-level document, or null when the project has none.</summary>
    ValueTask<AgentConfig?> ReadProjectAsync(
        string projectId,
        string adapterId,
        CancellationToken cancellationToken);

    /// <summary>Writes the user-level document atomically.</summary>
    ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken);

    /// <summary>Writes the project-level document atomically.</summary>
    ValueTask SaveProjectAsync(
        string projectId,
        AgentConfig config,
        CancellationToken cancellationToken);

    /// <summary>Lists the adapter ids that have a user-level document.</summary>
    ValueTask<IReadOnlyList<string>> ListUserAsync(CancellationToken cancellationToken);

    /// <summary>Lists the adapter ids that have a project-level document.</summary>
    ValueTask<IReadOnlyList<string>> ListProjectAsync(string projectId, CancellationToken cancellationToken);
}
