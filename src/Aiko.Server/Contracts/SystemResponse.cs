namespace Aiko.Server.Contracts;

/// <summary>
/// System information: daemon name, version, how it executes, PID and base URL.
/// </summary>
internal sealed record SystemResponse(
    string Name,
    string Version,
    string Runtime,
    int ProcessId,
    string BaseUrl,
    DateTimeOffset Time);
