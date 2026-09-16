namespace Aiko.Application.Agents;

/// <summary>
/// Result of processing a single file: status and error text when processing failed.
/// </summary>
public sealed record InstallationFileResult(
    string Path,
    InstallationFileStatus Status,
    string? Error);
