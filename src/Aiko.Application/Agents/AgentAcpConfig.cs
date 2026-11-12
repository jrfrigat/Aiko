namespace Aiko.Application.Agents;

/// <summary>How Aiko launches or connects to an agent over ACP.</summary>
/// <param name="Executable">The agent's ACP executable, or the launcher that starts it.</param>
/// <param name="Args">Arguments for <paramref name="Executable"/>.</param>
/// <param name="Protocol">ACP protocol version the adapter speaks.</param>
/// <param name="StartTimeoutSeconds">How long the launch and initialize may take.</param>
/// <param name="TurnTimeoutMinutes">How long one prompt may run before the session is treated as stuck.</param>
/// <param name="Environment">Environment variables added to the agent process.</param>
public sealed record AgentAcpConfig(
    string? Executable = null,
    IReadOnlyList<string>? Args = null,
    string Protocol = "v1",
    int StartTimeoutSeconds = 30,
    int TurnTimeoutMinutes = 120,
    IReadOnlyDictionary<string, string>? Environment = null);
