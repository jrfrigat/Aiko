namespace Aiko.Application.Contracts;

/// <summary>
/// A document of the durable project memory (.aiko/memory): relative path and content.
/// </summary>
public sealed record MemoryDocument(
    string Path,
    string Content,
    DateTimeOffset LastModifiedAt);

/// <summary>
/// One document of the project memory as a listing names it: where it is, how large, and when it last
/// changed. The content stays out of a listing; a screen reads the one document it opens.
/// </summary>
public sealed record MemoryDocumentSummary(
    string Path,
    long Size,
    DateTimeOffset LastModifiedAt);
