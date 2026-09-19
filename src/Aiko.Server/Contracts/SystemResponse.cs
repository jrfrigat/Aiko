using Aiko.Application.Contracts;

namespace Aiko.Server.Contracts;

/// <summary>
/// System information: daemon name, version, how it executes, PID and base URL.
/// </summary>
/// <param name="Name">Product name.</param>
/// <param name="Version">Version the daemon announces.</param>
/// <param name="Runtime">How it executes (framework and JIT/AOT).</param>
/// <param name="ProcessId">Operating-system process id.</param>
/// <param name="BaseUrl">The loopback endpoint it serves.</param>
/// <param name="Time">The daemon's clock, so a client can tell its own drift.</param>
/// <param name="Telemetry">The daemon's own runs: uptime, restarts, crashes and memory.</param>
/// <param name="AssetsVersion">
/// Version of the client bundle the daemon serves, or null when it serves no built client. A running client
/// compares it with the value it was loaded with to tell that it is older than the daemon.
/// </param>
internal sealed record SystemResponse(
    string Name,
    string Version,
    string Runtime,
    int ProcessId,
    string BaseUrl,
    DateTimeOffset Time,
    DaemonTelemetry Telemetry,
    string? AssetsVersion = null);
