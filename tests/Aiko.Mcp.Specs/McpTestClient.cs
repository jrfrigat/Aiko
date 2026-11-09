using ModelContextProtocol.Client;

namespace Aiko.Mcp.Specs;

/// <summary>
/// Creates the MCP clients the specs connect with.
/// </summary>
/// <remarks>
/// A client that leaves <see cref="McpClientOptions.ProtocolVersion"/> unset probes the server with
/// <c>server/discover</c> before the initialize fallback, and the probe is bounded by five seconds.
/// The stdio proxy runs as a separate process, so on a cold or loaded runner that budget can elapse
/// before the first answer arrives. The client then falls back to 2025-11-25 while the HTTP transport
/// still carries the 2026-07-28 header, and the daemon refuses the request with a 400 (TASK-380).
/// The specs need the protocol's default path, not a stopwatch, so every client they create gets a
/// probe budget that survives a cold start.
/// </remarks>
internal static class McpTestClient
{
    private static readonly McpClientOptions Options = new()
    {
        DiscoverProbeTimeout = TimeSpan.FromSeconds(30)
    };

    public static Task<McpClient> CreateAsync(
        IClientTransport transport,
        CancellationToken cancellationToken = default) =>
        McpClient.CreateAsync(transport, Options, cancellationToken: cancellationToken);
}
