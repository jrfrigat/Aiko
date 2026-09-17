namespace Aiko.Server.Contracts;

/// <summary>
/// Answer to a shutdown request: which process is stopping, so a caller can report what it stopped.
/// </summary>
/// <param name="ProcessId">Process id of the daemon that is shutting down.</param>
internal sealed record ShutdownResponse(int ProcessId);
