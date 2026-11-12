using Capastrum.Acp.Protocol;

namespace Aiko.Infrastructure.Agents;

/// <summary>Starts an ACP agent; a seam so the transport can be specified without a real process.</summary>
public interface IAcpClientFactory
{
    /// <summary>Launches the agent and initializes the ACP connection.</summary>
    Task<IAcpClientSession> StartAsync(
        AcpLaunchOptions options,
        CancellationToken cancellationToken);
}

/// <summary>Everything needed to launch one ACP agent.</summary>
/// <param name="Executable">Agent executable.</param>
/// <param name="Arguments">Executable arguments.</param>
/// <param name="WorkspaceRoot">Project root the agent works in.</param>
/// <param name="OnText">Called for every text update the agent streams.</param>
/// <param name="ReadTextFile">Reads a file the agent asked for; returns null when it does not exist.</param>
/// <param name="WriteTextFile">Writes a file the agent asked for.</param>
public sealed record AcpLaunchOptions(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkspaceRoot,
    Action<string> OnText,
    Func<string, CancellationToken, ValueTask<string?>> ReadTextFile,
    Func<string, string, CancellationToken, ValueTask> WriteTextFile);

/// <summary>One initialized ACP connection.</summary>
public interface IAcpClientSession : IAsyncDisposable
{
    /// <summary>Creates a session with the given MCP servers.</summary>
    ValueTask<string> NewSessionAsync(
        IReadOnlyList<AcpMcpServer> mcpServers,
        CancellationToken cancellationToken);

    /// <summary>Sends a prompt and waits for the turn to finish.</summary>
    ValueTask<AcpPromptResponse> SendPromptAsync(
        string sessionId,
        string prompt,
        CancellationToken cancellationToken);

    /// <summary>Closes the session.</summary>
    ValueTask CloseSessionAsync(string sessionId, CancellationToken cancellationToken);
}
