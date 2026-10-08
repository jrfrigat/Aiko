namespace Aiko.Application.Contracts;

/// <summary>
/// The project's semantic (vector) search over its memory, as one optional section of the settings.
/// </summary>
/// <remarks>
/// Off unless a project turns it on, and that default is the important half: a model is a download over a
/// hundred megabytes and building the index is an embedding run over the whole memory, so a machine that never
/// asked for semantic search must not pay for either. A project that states nothing states "no".
/// </remarks>
/// <param name="Enabled">Whether semantic search may be used at all.</param>
/// <param name="Provider">Which provider computes the vectors. Only <c>local</c> ships today.</param>
/// <param name="ModelId">Model to embed with; null means the model this build uses by default.</param>
/// <param name="MinimumScore">
/// Cosine a hit must reach to be returned. Below it a neighbour is noise dressed as a result, which is worse
/// than an empty answer.
/// </param>
public sealed record MemorySearchSettings(
    bool Enabled = false,
    string Provider = MemorySearchSettings.LocalProvider,
    string? ModelId = null,
    double MinimumScore = 0.2)
{
    /// <summary>The only provider this build implements: vectors computed on this machine, no service behind it.</summary>
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
/// One semantic hit: the document it is in, the chunk that matched, and how close it was.
/// </summary>
/// <remarks>
/// The chunk is named and not only the document because the point of semantic search is to reach a passage a
/// word-search would miss, and a reader who is handed the whole document still has to find it. The score is a
/// cosine in the model's own space - comparable within one model, meaningless across two - which is why it is
/// reported rather than hidden inside an ordering.
/// </remarks>
/// <param name="Path">Relative path of the memory document.</param>
/// <param name="Chunk">The section (or paragraph) of the document that matched.</param>
/// <param name="Score">Cosine similarity between the query and the chunk, from -1 to 1.</param>
public sealed record MemorySearchHit(string Path, string Chunk, double Score);

/// <summary>
/// Semantic search over the durable project memory: an index of vectors beside the FTS projection, and the
/// ranking that reads it.
/// </summary>
/// <remarks>
/// A module rather than the search itself: it can be turned off, and when it is, nothing here is reached and no
/// model is loaded. It is deliberately separate from <see cref="IMemoryStore"/>, which keeps the full-text
/// search working whether or not this exists - the two are two rankings of the same canonical Markdown, and
/// neither is allowed to become a second source of truth.
/// </remarks>
public interface IMemorySemanticSearch
{
    /// <summary>
    /// Rebuilds the vector index of a project from the documents it is given, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// Reads the effective settings itself: a project with the module off has its vectors cleared and nothing
    /// else happens, so disabling the module takes effect on the next reindex without loading a model.
    /// </remarks>
    /// <param name="projectId">Project whose index is being rebuilt.</param>
    /// <param name="documents">The canonical documents, as the reindexer read them from <c>.aiko/memory</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">
    /// The module is on but the model is not installed. The message says what to run.
    /// </exception>
    ValueTask RebuildAsync(
        string projectId,
        IReadOnlyList<MemoryDocument> documents,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ranks the project's indexed chunks against a query.
    /// </summary>
    /// <param name="projectId">Project to search.</param>
    /// <param name="query">What to look for, by meaning.</param>
    /// <param name="limit">Most hits to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The module is off for this project.</exception>
    /// <exception cref="FileNotFoundException">The module is on but the model is not installed.</exception>
    ValueTask<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string projectId,
        string query,
        int limit,
        CancellationToken cancellationToken);
}
