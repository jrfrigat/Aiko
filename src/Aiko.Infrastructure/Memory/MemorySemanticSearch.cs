using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// Semantic search over the durable project memory: it embeds a query, ranks the project's indexed chunks by
/// cosine, and drops what the module's score bar says is noise.
/// </summary>
/// <remarks>
/// The module's whole safety is in the two gates at the top of each method. It refuses rather than degrades -
/// a request for semantic search on a project that has it off gets a sentence saying so, not a silent fallback
/// to full text, because a caller that asked for meaning and received word matches would draw the wrong
/// conclusion about what was searched. And it loads a model only when it is about to use one, so a machine that
/// never turns the module on never pays for a session or a download.
/// </remarks>
public sealed class MemorySemanticSearch(
    IProjectCatalog projects,
    AikoDatabase database,
    IAppSettingsService settings,
    IEmbeddingProviderFactory providers) : IMemorySemanticSearch
{
    /// <summary>The most hits one search returns, the same bound the store and the surfaces keep.</summary>
    private const int MaximumResults = 100;

    private readonly SqliteMemoryVectorIndex index = new(database);

    /// <inheritdoc />
    public async ValueTask RebuildAsync(
        string projectId,
        IReadOnlyList<MemoryDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var project = await ResolveAsync(projectId, cancellationToken);
        var section = await settings.GetEffectiveMemorySearchAsync(project.Id, cancellationToken);

        if (!section.Enabled)
        {
            // Off means off: the vectors of a project that disabled the module are removed, not merely left
            // unread, so that turning it off takes effect on the very next reindex and leaves nothing behind.
            await index.ClearAsync(project.Id, cancellationToken);
            return;
        }

        var chunks = new List<(string Path, int Index, string Content)>();
        foreach (var document in documents)
        {
            var pieces = MemoryMarkdownChunker.Chunk(document.Content);
            for (var position = 0; position < pieces.Count; position++)
            {
                chunks.Add((document.Path, position, pieces[position]));
            }
        }

        if (chunks.Count == 0)
        {
            await index.ClearAsync(project.Id, cancellationToken);
            return;
        }

        var provider = providers.Create(section.ModelId);
        try
        {
            var vectors = await provider.EmbedAsync(
                chunks.Select(chunk => chunk.Content).ToList(), cancellationToken);
            var rows = chunks
                .Select((chunk, position) => new IndexedChunk(
                    chunk.Path, chunk.Index, chunk.Content, vectors[position]))
                .ToList();
            await index.ReplaceAsync(
                project.Id, provider.ModelId, provider.Dimensions, rows, cancellationToken);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string projectId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaximumResults);

        var project = await ResolveAsync(projectId, cancellationToken);
        var section = await settings.GetEffectiveMemorySearchAsync(project.Id, cancellationToken);
        if (!section.Enabled)
        {
            throw new InvalidOperationException(
                "Semantic memory search is off for this project. Turn it on in the project's settings " +
                "(memorySearch.enabled) and run `aiko reindex`.");
        }

        var provider = providers.Create(section.ModelId);
        try
        {
            var queryVector = (await provider.EmbedAsync([query], cancellationToken))[0];
            var indexed = await index.LoadAsync(project.Id, provider.ModelId, cancellationToken);
            return indexed
                .Select(chunk => new MemorySearchHit(
                    chunk.Path, chunk.Content, Dot(queryVector, chunk.Vector)))
                .Where(hit => hit.Score >= section.MinimumScore)
                .OrderByDescending(hit => hit.Score)
                .ThenBy(hit => hit.Path, StringComparer.Ordinal)
                .ThenBy(hit => hit.Chunk, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Cosine of two vectors. The provider normalises its vectors to unit length, so the dot product already is
    /// the cosine and a second normalisation would only recompute what is known.
    /// </summary>
    private static double Dot(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var sum = 0d;
        for (var position = 0; position < length; position++)
        {
            sum += (double)left[position] * right[position];
        }

        return sum;
    }

    private async ValueTask<RegisteredProject> ResolveAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
}
