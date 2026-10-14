namespace Aiko.Application.Contracts;

/// <summary>
/// The project's semantic (vector) search over its memory, as one optional section of the settings. Off
/// unless a project turns it on, and that default is the point: the model is a download over a hundred
/// megabytes and building the index is an embedding run over the whole memory, so a machine that never asked
/// for semantic search must not pay for either. <c>MinimumScore</c> is the cosine a hit must reach - below it
/// a neighbour is noise dressed as a result, which is worse than an empty answer.
/// </summary>
public sealed record MemorySearchSettings(
    bool Enabled = false,
    string Provider = MemorySearchSettings.LocalProvider,
    string? ModelId = null,
    double MinimumScore = 0.2)
{
    /// <summary>The only provider this build implements: vectors computed on this machine.</summary>
    public const string LocalProvider = "local";

    /// <summary>What a project that states nothing gets: the module off.</summary>
    public static MemorySearchSettings SafeDefault { get; } = new();
}

/// <summary>Which ranking the memory search uses.</summary>
public enum MemorySearchMode
{
    /// <summary>Full-text search (SQLite FTS5): matches words. The default, and the only mode that needs no model.</summary>
    Fts,

    /// <summary>Vector search: matches meaning. Requires the module to be on and its model to be installed.</summary>
    Semantic
}

/// <summary>
/// One semantic hit: the document it is in, the chunk that matched, and how close it was. The chunk is named
/// and not only the document because the point of semantic search is to reach a passage a word-search would
/// miss, and the score is a cosine in the model's own space - comparable within one model, meaningless across
/// two - which is why it is reported rather than hidden inside an ordering.
/// </summary>
public sealed record MemorySearchHit(string Path, string Chunk, double Score);

/// <summary>
/// Semantic search over the durable project memory: an index of vectors beside the FTS projection, and the
/// ranking that reads it. A module rather than the search itself, separate from <see cref="IMemoryStore"/> -
/// the two are two rankings of the same canonical Markdown, and neither may become a second source of truth.
/// </summary>
public interface IMemorySemanticSearch
{
    /// <summary>
    /// Rebuilds the vector index of a project from the documents it is given, replacing whatever was there.
    /// Reads the effective settings itself: a project with the module off has its vectors cleared and nothing
    /// else happens, so disabling the module takes effect on the next reindex without loading a model.
    /// </summary>
    /// <exception cref="FileNotFoundException">The module is on but the model is not installed; the message
    /// says what to run.</exception>
    ValueTask RebuildAsync(
        string projectId,
        IReadOnlyList<MemoryDocument> documents,
        CancellationToken cancellationToken);

    /// <summary>Ranks the project's indexed chunks against a query, most relevant first, at most
    /// <paramref name="limit"/> of them.</summary>
    /// <exception cref="InvalidOperationException">The module is off for this project.</exception>
    /// <exception cref="FileNotFoundException">The module is on but the model is not installed.</exception>
    ValueTask<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string projectId,
        string query,
        int limit,
        CancellationToken cancellationToken);
}
