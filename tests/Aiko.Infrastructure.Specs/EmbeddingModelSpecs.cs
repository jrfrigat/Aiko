using System.Net;
using System.Security.Cryptography;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Installation;
using Aiko.Infrastructure.Models;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The model installer: a file appears only if it verifies, whichever of the two delivery paths fetched it.
/// </summary>
/// <remarks>
/// These specs never touch the network. The release path is a local feed - the same <c>IReleaseSource</c> a
/// mirrored installation uses, not a test double - and the external path is an <see cref="HttpClient"/> over a
/// handler that answers from memory. The model under test is small and synthetic, with its checksums computed
/// from its own bytes: what is being checked is that both paths are held to one rule, not that a particular
/// file weighs 118 MB.
/// </remarks>
public sealed class EmbeddingModelSpecs
{
    [Fact]
    public async Task A_model_nobody_asked_for_is_absent_and_nothing_is_fetched()
    {
        await using var fixture = ModelFixture.Create();

        var status = await fixture.Installer.InspectAsync(ModelFixture.ModelId, CancellationToken.None);

        Assert.False(status.Present);
        Assert.Equal(0, status.Bytes);
        Assert.Contains("not present", status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.ModelDirectory));
        // Reading the installation must never reach the network, whatever is or is not on disk.
        Assert.Equal(0, fixture.ExternalRequests);
    }

    [Fact]
    public async Task The_release_path_installs_a_model_that_verifies()
    {
        await using var fixture = ModelFixture.Create();

        var installation = await fixture.Installer.EnsureAsync(
            ModelFixture.ModelId,
            EmbeddingModelSource.Release,
            CancellationToken.None);

        Assert.True(installation.Present, installation.Detail);
        Assert.True(installation.Downloaded, installation.Detail);
        Assert.Equal(0, fixture.ExternalRequests);
        await fixture.AssertInstalledAsync();

        var status = await fixture.Installer.InspectAsync(ModelFixture.ModelId, CancellationToken.None);
        Assert.True(status.Present, status.Detail);
        Assert.Equal(fixture.TotalBytes, status.Bytes);
    }

    [Fact]
    public async Task The_external_path_installs_the_same_model()
    {
        await using var fixture = ModelFixture.Create();

        var installation = await fixture.Installer.EnsureAsync(
            ModelFixture.ModelId,
            EmbeddingModelSource.External,
            CancellationToken.None);

        Assert.True(installation.Present, installation.Detail);
        await fixture.AssertInstalledAsync();
        Assert.True(fixture.ExternalRequests > 0);
    }

    [Fact]
    public async Task Asking_twice_downloads_nothing_the_second_time()
    {
        await using var fixture = ModelFixture.Create();
        await fixture.Installer.EnsureAsync(ModelFixture.ModelId, EmbeddingModelSource.External, CancellationToken.None);
        var requestsAfterFirst = fixture.ExternalRequests;

        var second = await fixture.Installer.EnsureAsync(
            ModelFixture.ModelId,
            EmbeddingModelSource.External,
            CancellationToken.None);

        Assert.True(second.Present, second.Detail);
        Assert.False(second.Downloaded, second.Detail);
        Assert.Contains("Already installed", second.Detail, StringComparison.Ordinal);
        Assert.Equal(requestsAfterFirst, fixture.ExternalRequests);
    }

    [Fact]
    public async Task A_file_whose_bytes_do_not_match_is_never_put_in_place()
    {
        await using var fixture = ModelFixture.Create();
        // Same length as the real bytes, different content: the failure the checksum exists for, and the one a
        // size alone can never catch.
        fixture.ServeExternal(ModelFixture.GraphName, "xxxxx-bytes-for-the-spec"u8.ToArray());

        var installation = await fixture.Installer.EnsureAsync(
            ModelFixture.ModelId,
            EmbeddingModelSource.External,
            CancellationToken.None);

        Assert.False(installation.Present, installation.Detail);
        Assert.Contains("wrong checksum", installation.Detail, StringComparison.Ordinal);
        // The choice is between a model that verifies and no model - never a wrong one left behind.
        Assert.False(File.Exists(fixture.PathOf(ModelFixture.GraphName)));
        Assert.False(File.Exists(fixture.PathOf(ModelFixture.GraphName) + ".part"));
    }

    [Fact]
    public async Task A_truncated_download_is_refused_by_its_size()
    {
        await using var fixture = ModelFixture.Create();
        fixture.ServeExternal(ModelFixture.GraphName, "short"u8.ToArray());

        var installation = await fixture.Installer.EnsureAsync(
            ModelFixture.ModelId,
            EmbeddingModelSource.External,
            CancellationToken.None);

        Assert.False(installation.Present, installation.Detail);
        Assert.Contains("wrong size", installation.Detail, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.PathOf(ModelFixture.GraphName)));
    }

    [Fact]
    public async Task Removing_a_model_takes_its_files_away_and_says_so_twice()
    {
        await using var fixture = ModelFixture.Create();
        await fixture.Installer.EnsureAsync(ModelFixture.ModelId, EmbeddingModelSource.Release, CancellationToken.None);

        var removed = await fixture.Installer.RemoveAsync(ModelFixture.ModelId, CancellationToken.None);
        var again = await fixture.Installer.RemoveAsync(ModelFixture.ModelId, CancellationToken.None);

        Assert.False(removed.Present);
        Assert.False(Directory.Exists(fixture.ModelDirectory));
        // An empty case said in words rather than left as silence, and not a failure either.
        Assert.Contains("nothing to remove", again.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_model_is_kept_beside_the_database_and_not_in_a_project()
    {
        await using var fixture = ModelFixture.Create();

        await fixture.Installer.EnsureAsync(ModelFixture.ModelId, EmbeddingModelSource.Release, CancellationToken.None);

        // Installation data, not project data: one copy serves every project, and deleting the directory is a
        // legitimate way to get the space back.
        var expected = Path.Combine(fixture.DataDirectory, "models", ModelFixture.ModelId, ModelFixture.GraphName);
        Assert.True(File.Exists(expected), expected);
    }

    [Fact]
    public async Task A_model_this_build_does_not_know_is_refused_by_name()
    {
        await using var fixture = ModelFixture.Create();

        var status = await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Installer.InspectAsync("no-such-model", CancellationToken.None));

        Assert.Contains("no-such-model", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_built_in_registry_records_the_checksums_the_publisher_states()
    {
        var model = Assert.Single(new BuiltInEmbeddingModelCatalog().Models);

        Assert.Equal(BuiltInEmbeddingModelCatalog.MultilingualMiniLmId, model.Id);
        Assert.Equal("apache-2.0", model.License);
        Assert.NotEmpty(model.ReleaseTag);
        Assert.Equal(2, model.Files.Count);

        foreach (var file in model.Files)
        {
            // A checksum is only usable as one if it is the right shape: 64 lower-case hex characters.
            Assert.Equal(64, file.Sha256.Length);
            Assert.All(file.Sha256, character => Assert.Contains(character, "0123456789abcdef"));
            Assert.True(file.SizeBytes > 0);
            // Pinned to a revision rather than a branch: bytes behind a branch can change under a fixed hash.
            Assert.StartsWith("https://huggingface.co/", file.ExternalUrl, StringComparison.Ordinal);
            Assert.Contains("/resolve/2c4055b12046f11709e9df2c122e59ffbdc2f900/", file.ExternalUrl, StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// A model small enough to install inside a spec: two files whose checksums are computed from their own bytes,
/// a local release feed serving them, and an <see cref="HttpClient"/> whose handler answers from memory.
/// </summary>
internal sealed class ModelFixture : IAsyncDisposable
{
    /// <summary>Identifier the model is registered under here.</summary>
    public const string ModelId = "spec-model";

    /// <summary>Name of the graph file.</summary>
    public const string GraphName = "graph.onnx";

    /// <summary>Name of the tokenizer file.</summary>
    public const string TokenizerName = "tokenizer.json";

    private const string ReleaseTag = "models-spec";
    private const string ExternalRoot = "https://models.example.invalid/";

    private readonly string root;
    private readonly string feedRoot;
    private readonly StubHttpHandler handler;
    private readonly HttpClient http;

    private ModelFixture(
        string root,
        string feedRoot,
        StubHttpHandler handler,
        HttpClient http,
        AikoDataPaths paths,
        EmbeddingModel model,
        EmbeddingModelInstaller installer)
    {
        this.root = root;
        this.feedRoot = feedRoot;
        this.handler = handler;
        this.http = http;
        Paths = paths;
        Model = model;
        Installer = installer;
    }

    /// <summary>Data paths of this fixture's installation.</summary>
    public AikoDataPaths Paths { get; }

    /// <summary>The model under test.</summary>
    public EmbeddingModel Model { get; }

    /// <summary>The installer under test.</summary>
    public EmbeddingModelInstaller Installer { get; }

    /// <summary>Directory the daemon's data would live in.</summary>
    public string DataDirectory => Path.GetDirectoryName(Paths.DatabasePath)!;

    /// <summary>Directory the model is installed into.</summary>
    public string ModelDirectory => Paths.ModelDirectory(ModelId);

    /// <summary>How many requests the external path made.</summary>
    public int ExternalRequests => handler.Requests;

    /// <summary>Total size the model's files should take.</summary>
    public long TotalBytes => Model.SizeBytes;

    /// <summary>Builds the fixture, with both paths serving the correct bytes to begin with.</summary>
    public static ModelFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-model-{Guid.NewGuid():N}");
        var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
        var feedRoot = Path.Combine(root, "feed");

        var model = new EmbeddingModel(
            ModelId,
            "apache-2.0",
            ReleaseTag,
            [
                Describe(GraphName, ContentOf(GraphName)),
                Describe(TokenizerName, ContentOf(TokenizerName))
            ]);

        var handler = new StubHttpHandler();
        var http = new HttpClient(handler);
        var installer = new EmbeddingModelInstaller(
            paths,
            [model],
            [
                new ReleaseChannelModelDownloader(new LocalReleaseSource(feedRoot)),
                new ExternalModelDownloader(http)
            ]);

        var fixture = new ModelFixture(root, feedRoot, handler, http, paths, model, installer);
        foreach (var file in model.Files)
        {
            fixture.WriteFeed(file.Name, ContentOf(file.Name));
            fixture.ServeExternal(file.Name, ContentOf(file.Name));
        }

        return fixture;
    }

    /// <summary>The bytes a file of the model is made of here.</summary>
    public static byte[] ContentOf(string fileName) =>
        string.Equals(fileName, GraphName, StringComparison.Ordinal)
            ? "graph-bytes-for-the-spec"u8.ToArray()
            : "tokenizer-json-for-the-spec"u8.ToArray();

    /// <summary>Where a file of the model is expected to land.</summary>
    public string PathOf(string fileName) => Path.Combine(ModelDirectory, fileName);

    /// <summary>Makes the release feed serve other bytes for a file.</summary>
    public void WriteFeed(string fileName, byte[] content)
    {
        var directory = Path.Combine(feedRoot, ReleaseTag);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, fileName), content);
    }

    /// <summary>Makes the external address answer with other bytes for a file.</summary>
    public void ServeExternal(string fileName, byte[] content) =>
        handler.Serve($"{ExternalRoot}{fileName}", content);

    /// <summary>Asserts every file is on disk with exactly the bytes the model is made of.</summary>
    public async Task AssertInstalledAsync()
    {
        foreach (var file in Model.Files)
        {
            var path = PathOf(file.Name);
            Assert.True(File.Exists(path), path);
            Assert.Equal(ContentOf(file.Name), await File.ReadAllBytesAsync(path));
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        http.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static EmbeddingModelFile Describe(string name, byte[] content) =>
        new(name, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)), $"{ExternalRoot}{name}");
}

/// <summary>
/// An HTTP handler that answers from a dictionary, so the external path can be exercised without a network and
/// with a counted number of requests.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> responses = new(StringComparer.Ordinal);

    /// <summary>How many requests were made.</summary>
    public int Requests { get; private set; }

    /// <summary>Sets the bytes a URL answers with.</summary>
    public void Serve(string url, byte[] content) => responses[url] = content;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests++;
        var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        return Task.FromResult(responses.TryGetValue(url, out var content)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
