namespace Aiko.Server.Contracts;

/// <summary>
/// Artifact version conflict response for optimistic saves.
/// </summary>
internal sealed record ArtifactConflictResponse(string? ExpectedVersion, string? ActualVersion);
