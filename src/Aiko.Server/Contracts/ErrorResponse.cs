namespace Aiko.Server.Contracts;

/// <summary>
/// Standard error response with a message.
/// </summary>
internal sealed record ErrorResponse(string Message);
