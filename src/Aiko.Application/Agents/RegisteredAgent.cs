namespace Aiko.Application.Agents;

/// <summary>An agent the daemon knows, with the state that decides whether it can take work now.</summary>
/// <param name="Config">Effective configuration.</param>
/// <param name="Installations">Installations found on this machine.</param>
/// <param name="IsAvailable">Whether the configured transport is usable right now.</param>
/// <param name="ActiveSessions">Direct sessions currently running for the agent.</param>
public sealed record RegisteredAgent(
    EffectiveAgentConfig Config,
    IReadOnlyList<AgentInstallation> Installations,
    bool IsAvailable,
    int ActiveSessions)
{
    /// <summary>True when at least one installation was found.</summary>
    public bool IsInstalled => Installations.Count > 0;
}
