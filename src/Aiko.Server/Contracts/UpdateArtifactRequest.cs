namespace Aiko.Server.Contracts;

/// <summary>
/// Card artifact save request with an optimistic version.
/// </summary>
internal sealed record UpdateArtifactRequest(
    string Path,
    string Content,
    string? ExpectedVersion);
