namespace Aiko.Application.Agents;

/// <summary>Resolves, validates and saves agent configurations.</summary>
public interface IAgentConfigService
{
    /// <summary>Merges the adapter default, the user layer and the project layer.</summary>
    ValueTask<EffectiveAgentConfig> GetEffectiveAsync(
        string? projectId,
        string adapterId,
        CancellationToken cancellationToken);

    /// <summary>Resolves every adapter the daemon knows.</summary>
    ValueTask<IReadOnlyList<EffectiveAgentConfig>> ListEffectiveAsync(
        string? projectId,
        CancellationToken cancellationToken);

    /// <summary>Validates and writes one user-level layer.</summary>
    ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken);

    /// <summary>Validates and writes one project-level layer.</summary>
    ValueTask SaveProjectAsync(
        string projectId,
        AgentConfig config,
        CancellationToken cancellationToken);

    /// <summary>Throws when the effective configuration cannot be used.</summary>
    void Validate(EffectiveAgentConfig config);
}
