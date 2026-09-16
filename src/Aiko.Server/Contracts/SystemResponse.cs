namespace Aiko.Server.Contracts;

/// <summary>
/// System information: daemon name, version, PID and base URL.
/// </summary>
internal sealed record SystemResponse(
    string Name,
    string Version,
    int ProcessId,
    string BaseUrl,
    DateTimeOffset Time);
