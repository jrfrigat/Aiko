namespace Aiko.Application.Agents;

/// <summary>Capabilities supported by a specific AI agent adapter.</summary>
[Flags]
public enum AgentCapabilities
{
    /// <summary>No capabilities.</summary>
    None = 0,

    /// <summary>MCP stdio transport.</summary>
    McpStdio = 1 << 0,

    /// <summary>MCP Streamable HTTP transport.</summary>
    McpStreamableHttp = 1 << 1,

    /// <summary>Workspace-level configuration (files in the project root).</summary>
    WorkspaceConfiguration = 1 << 2,

    /// <summary>Skills support.</summary>
    Skills = 1 << 3,

    /// <summary>Custom commands support.</summary>
    Commands = 1 << 4,

    /// <summary>Hooks support.</summary>
    Hooks = 1 << 5,

    /// <summary>Headless launch support.</summary>
    HeadlessLaunch = 1 << 6,

    /// <summary>Ability to resume an interrupted session.</summary>
    Resume = 1 << 7,

    /// <summary>Structured result output.</summary>
    StructuredOutput = 1 << 8,

    /// <summary>Rate-limit detection from process output.</summary>
    RateLimitDetection = 1 << 9,

    /// <summary>Remote workspace (no local checkout required).</summary>
    RemoteWorkspace = 1 << 10,

    /// <summary>The agent can take commands from the project queue.</summary>
    CommandQueue = 1 << 11,

    /// <summary>The agent can be started or reached over the Agent Client Protocol.</summary>
    AcpTransport = 1 << 12,

    /// <summary>The agent can be started through an SDK adapter.</summary>
    SdkTransport = 1 << 13,

    /// <summary>The agent reaches Aiko over MCP.</summary>
    AikoMcp = 1 << 14,

    /// <summary>The agent reaches Aiko over REST.</summary>
    AikoRest = 1 << 15,

    /// <summary>The agent reaches Aiko through the CLI.</summary>
    AikoCli = 1 << 16
}
