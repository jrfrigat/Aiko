namespace Aiko.Application.Contracts;

/// <summary>
/// Durable project memory: Markdown documents with decisions, conventions and lessons.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Searches documents containing the query text (case-insensitive),
    /// returning at most <paramref name="limit"/> results.
    /// </summary>
    ValueTask<IReadOnlyList<MemoryDocument>> SearchAsync(
        string projectId,
        string query,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a document by relative Markdown path inside .aiko/memory.
    /// </summary>
    ValueTask StoreAsync(
        string projectId,
        string path,
        string content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every Markdown document under .aiko/memory, read from the files themselves: they are the memory, the
    /// search index is only a projection of them.
    /// </summary>
    ValueTask<IReadOnlyList<MemoryDocumentSummary>> ListAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// One document by relative Markdown path, or null when there is none.
    /// </summary>
    ValueTask<MemoryDocument?> ReadAsync(
        string projectId,
        string path,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a document; a missing document is not an error.
    /// </summary>
    ValueTask RemoveAsync(
        string projectId,
        string path,
        CancellationToken cancellationToken);
}
