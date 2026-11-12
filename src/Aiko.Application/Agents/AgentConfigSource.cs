namespace Aiko.Application.Agents;

/// <summary>Where an effective configuration value came from.</summary>
public enum AgentConfigSource
{
    /// <summary>The adapter's own default.</summary>
    Default,

    /// <summary>The user-level file.</summary>
    User,

    /// <summary>The project-level file.</summary>
    Project
}
