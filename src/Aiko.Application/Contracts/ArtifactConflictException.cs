namespace Aiko.Application.Contracts;

/// <summary>
/// Version conflict while saving an artifact: the expected version did not match the actual one.
/// </summary>
public sealed class ArtifactConflictException(
    string path,
    string? expectedVersion,
    string? actualVersion)
    : InvalidOperationException($"Artifact conflict for {path}.")
{
    /// <summary>
    /// Version expected by the caller; null means creating a new file.
    /// </summary>
    public string? ExpectedVersion { get; } = expectedVersion;

    /// <summary>
    /// Actual version of the file; null when the file does not exist.
    /// </summary>
    public string? ActualVersion { get; } = actualVersion;
}
