namespace Aiko.Application.Contracts;

/// <summary>
/// Content of a card Markdown artifact with a version (SHA-256 of the content)
/// used for optimistic conflict control.
/// </summary>
public sealed record CardArtifactDocument(
    string Path,
    string Content,
    string Version,
    DateTimeOffset LastModifiedAt);
