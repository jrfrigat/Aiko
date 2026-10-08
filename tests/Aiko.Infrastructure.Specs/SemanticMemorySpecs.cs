using Aiko.Application.Contracts;
using Aiko.Infrastructure.Memory;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The semantic memory search: the chunker, the vector index, the reindex that rebuilds it, and the ranking.
/// </summary>
/// <remarks>
/// Nothing here loads the real model: it weighs 118 MB and a spec that needed it is a spec nobody runs. What is
/// checked is everything around the provider - that a document is split the way a reader would recognise, that a
/// reindex replaces rather than appends, that a disabled module leaves nothing behind, and that a query is
/// ranked by meaning over the stored vectors. The provider is a deterministic stand-in, so the vectors are the
/// spec's own numbers; whether the real model's pooling matches its training is a manual comparison.
/// </remarks>
public sealed class SemanticMemorySpecs
{
    [Fact]
    public void A_document_is_split_on_its_headings()
    {
        var chunks = MemoryMarkdownChunker.Chunk("# One\nfirst\n\n# Two\nsecond\n");

        Assert.Equal(["# One\nfirst", "# Two\nsecond"], chunks);
    }

    [Fact]
    public void An_empty_document_yields_no_chunks()
    {
        Assert.Empty(MemoryMarkdownChunker.Chunk("   \n  \n"));
    }

    [Fact]
    public void A_long_section_is_broken_at_a_blank_line_rather_than_mid_sentence()
    {
        var text = new string('x', MemoryMarkdownChunker.MaxChunkCharacters) + "\n\ntail";

        var chunks = MemoryMarkdownChunker.Chunk(text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new string('x', MemoryMarkdownChunker.MaxChunkCharacters), chunks[0]);
        Assert.Equal("tail", chunks[1]);
    }

    [Fact]
    public async Task The_module_is_off_until_a_project_turns_it_on()
    {
        await RunAsync(async harness =>
        {
            var section = await harness.Settings.GetEffectiveMemorySearchAsync(
                harness.Project.Id, CancellationToken.None);

            Assert.False(section.Enabled);
            Assert.False(MemorySearchSettings.SafeDefault.Enabled);
        });
    }

    [Fact]
    public async Task A_semantic_search_on_a_project_with_the_module_off_is_refused()
    {
        await RunAsync(async harness =>
        {
            var search = harness.Semantic(new HashingEmbeddingProvider());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await search.SearchAsync(harness.Project.Id, "roof", 10, CancellationToken.None));

            Assert.Contains("off", exception.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task An_enabled_reindex_indexes_the_memory_and_a_query_is_ranked_by_meaning()
    {
        await RunAsync(async harness =>
        {
            await EnableAsync(harness);
            await harness.Memory.StoreAsync(harness.Project.Id, "decisions/roof.md",
                "# Roof\n\nThe roof is leaking water into the attic.\n", CancellationToken.None);
            await harness.Memory.StoreAsync(harness.Project.Id, "conventions/database.md",
                "# Database\n\nTable names follow the singular convention.\n", CancellationToken.None);

            var provider = new HashingEmbeddingProvider();
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);

            var hits = await harness.Semantic(provider).SearchAsync(
                harness.Project.Id, "roof leaking water", 10, CancellationToken.None);

            Assert.NotEmpty(hits);
            Assert.Equal("decisions/roof.md", hits[0].Path);
            Assert.True(hits[0].Score > 0);
        });
    }

    [Fact]
    public async Task The_full_text_search_keeps_working_alongside_the_vector_index()
    {
        await RunAsync(async harness =>
        {
            await EnableAsync(harness);
            await harness.Memory.StoreAsync(harness.Project.Id, "decisions/roof.md",
                "The roof is leaking.", CancellationToken.None);

            var provider = new HashingEmbeddingProvider();
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);

            var documents = await harness.Memory.SearchAsync(
                harness.Project.Id, "roof", 10, CancellationToken.None);

            Assert.Single(documents);
            Assert.Equal("decisions/roof.md", documents[0].Path);
        });
    }

    [Fact]
    public async Task A_removed_document_is_no_longer_ranked_after_a_reindex()
    {
        await RunAsync(async harness =>
        {
            await EnableAsync(harness);
            await harness.Memory.StoreAsync(harness.Project.Id, "notes.md",
                "roof leaking water", CancellationToken.None);

            var provider = new HashingEmbeddingProvider();
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);
            Assert.NotEmpty(await harness.Semantic(provider).SearchAsync(
                harness.Project.Id, "roof", 10, CancellationToken.None));

            await harness.Memory.RemoveAsync(harness.Project.Id, "notes.md", CancellationToken.None);
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);

            // The reindex replaces: the vector of the deleted document is gone, not left to rank.
            Assert.Empty(await harness.Semantic(provider).SearchAsync(
                harness.Project.Id, "roof", 10, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Turning_the_module_off_rebuilds_the_index_to_empty()
    {
        await RunAsync(async harness =>
        {
            await EnableAsync(harness);
            await harness.Memory.StoreAsync(harness.Project.Id, "a.md", "roof leaking", CancellationToken.None);

            var provider = new HashingEmbeddingProvider();
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);
            Assert.NotEmpty(await harness.Semantic(provider).SearchAsync(
                harness.Project.Id, "roof", 10, CancellationToken.None));

            await harness.SettingsStore.SaveProjectAsync(
                harness.Project.Id,
                new AppSettings(
                    AppSettings.CurrentSchemaVersion,
                    MemorySearch: new MemorySearchSettings(Enabled: false)),
                CancellationToken.None);
            await harness.Reindexer(provider).ReindexAsync(harness.Project.Id, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Semantic(provider).SearchAsync(
                    harness.Project.Id, "roof", 10, CancellationToken.None));
        });
    }

    [Fact]
    public async Task An_enabled_project_without_its_model_reports_what_to_run()
    {
        await RunAsync(async harness =>
        {
            await EnableAsync(harness);
            await harness.Memory.StoreAsync(harness.Project.Id, "a.md", "roof", CancellationToken.None);

            var semantic = new MemorySemanticSearch(
                harness.Catalog, harness.Database, harness.Settings, new MissingModelFactory());
            var reindexer = new ProjectReindexer(harness.Catalog, harness.Database, semantic);

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await reindexer.ReindexAsync(harness.Project.Id, CancellationToken.None));

            Assert.Contains("aiko model download", exception.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>A factory whose model is missing, standing in for the real one before it is downloaded.</summary>
    private sealed class MissingModelFactory : IEmbeddingProviderFactory
    {
        public IEmbeddingProvider Create(string? modelId) =>
            throw new FileNotFoundException(
                $"The embedding model '{modelId ?? "default"}' is not installed. Run `aiko model download`.",
                modelId);
    }

    /// <summary>A project, its database and the settings, built the way the daemon builds them.</summary>
    private sealed record Harness(
        string Root,
        RegisteredProject Project,
        AikoDatabase Database,
        IProjectCatalog Catalog,
        AppSettingsService Settings,
        IAppSettingsStore SettingsStore,
        FileMemoryStore Memory)
    {
        public MemorySemanticSearch Semantic(IEmbeddingProvider provider) =>
            new(Catalog, Database, Settings, new StubEmbeddingProviderFactory(provider));

        public ProjectReindexer Reindexer(IEmbeddingProvider provider) =>
            new(Catalog, Database, Semantic(provider));
    }

    private static async Task RunAsync(Func<Harness, Task> assertion)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"aiko-semantic-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(testRoot, "project");
        Directory.CreateDirectory(projectRoot);

        try
        {
            var paths = new AikoDataPaths(Path.Combine(testRoot, "data", "aiko.db"));
            var database = new AikoDatabase(paths);
            await database.InitializeAsync();
            var catalog = new SqliteProjectCatalog(database);
            var settingsStore = new FileAppSettingsStore(catalog);
            var reindexer = new ProjectReindexer(catalog, database);
            var initializer = new ProjectInitializer(
                catalog, reindexer, settingsStore, new FileProjectTemplateStore(paths));
            var project = await initializer.InitializeAsync(
                new InitializeProjectRequest(projectRoot), CancellationToken.None);

            await assertion(new Harness(
                testRoot,
                project,
                database,
                catalog,
                new AppSettingsService(settingsStore),
                settingsStore,
                new FileMemoryStore(catalog, database)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    private static async Task EnableAsync(Harness harness, double minimumScore = 0.1) =>
        await harness.SettingsStore.SaveProjectAsync(
            harness.Project.Id,
            new AppSettings(
                AppSettings.CurrentSchemaVersion,
                MemorySearch: new MemorySearchSettings(
                    Enabled: true,
                    Provider: MemorySearchSettings.LocalProvider,
                    ModelId: HashingEmbeddingProvider.Id,
                    MinimumScore: minimumScore)),
            CancellationToken.None);

    /// <summary>Hands the search the same provider every time, so a spec's vectors are its own numbers.</summary>
    private sealed class StubEmbeddingProviderFactory(IEmbeddingProvider provider) : IEmbeddingProviderFactory
    {
        public IEmbeddingProvider Create(string? modelId) => provider;
    }

    /// <summary>
    /// A deterministic, dependency-free stand-in for the real model: a hashing bag of words, normalised.
    /// </summary>
    /// <remarks>
    /// Deliberately lexical, and not a claim about meaning. Its job is to give the spec two texts that share
    /// words close vectors and two that do not, so the code under test - storage, replacement, cosine and the
    /// score bar - is exercised end to end without a model.
    /// </remarks>
    private sealed class HashingEmbeddingProvider : IEmbeddingProvider
    {
        public const string Id = "hashing-stand-in";
        private const int Size = 64;

        public string ModelId => Id;

        public int Dimensions => Size;

        public ValueTask<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            var vectors = new List<float[]>(texts.Count);
            foreach (var text in texts)
            {
                var vector = new float[Size];
                foreach (var token in text.ToLowerInvariant()
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    vector[Hash(token) % Size] += 1f;
                }

                var sumOfSquares = 0d;
                foreach (var value in vector)
                {
                    sumOfSquares += (double)value * value;
                }

                var length = Math.Sqrt(sumOfSquares);
                if (length > 0)
                {
                    for (var index = 0; index < Size; index++)
                    {
                        vector[index] = (float)(vector[index] / length);
                    }
                }

                vectors.Add(vector);
            }

            return ValueTask.FromResult<IReadOnlyList<float[]>>(vectors);
        }

        private static int Hash(string token)
        {
            var hash = 17;
            foreach (var character in token)
            {
                hash = (hash * 31 + character) & 0x7fffffff;
            }

            return hash;
        }
    }
}
