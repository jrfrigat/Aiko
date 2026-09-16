namespace Aiko.Application.Contracts;

/// <summary>
/// Brief description of a card artifact in a listing.
/// </summary>
public sealed record CardArtifactSummary(
    string Path,
    long Size,
    DateTimeOffset LastModifiedAt);
