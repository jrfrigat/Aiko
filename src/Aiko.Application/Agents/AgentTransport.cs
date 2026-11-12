namespace Aiko.Application.Agents;

/// <summary>How a command reaches an agent.</summary>
public enum AgentTransport
{
    /// <summary>The agent takes the command from the project queue.</summary>
    Command,

    /// <summary>Aiko starts or connects to the agent over the Agent Client Protocol.</summary>
    Acp,

    /// <summary>Aiko starts the agent through its SDK adapter.</summary>
    Sdk
}
