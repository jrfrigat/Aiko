namespace Aiko.Server.Contracts;

/// <summary>
/// Request to pair the local browser with the daemon using a one-time pairing code.
/// </summary>
public sealed record PairRequest(string Code);

/// <summary>
/// Response containing a freshly generated one-time pairing code.
/// </summary>
public sealed record PairResponse(string Code);