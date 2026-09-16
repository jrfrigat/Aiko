namespace Aiko.Application.Contracts;

/// <summary>
/// A document of the durable project memory (.aiko/memory): relative path and content.
/// </summary>
public sealed record MemoryDocument(
    string Path,
    string Content,
    DateTimeOffset LastModifiedAt);
