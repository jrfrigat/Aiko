using Aiko.Application.Contracts;
using Aiko.Infrastructure.Releases;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The release history: what the project released, in the project's own file, and what it refuses to record.
/// </summary>
/// <remarks>
/// The acceptance asks that recording and reading work and that the refusal rules are checked. Both are held
/// here, against a real project on disk rather than a stub, because the point of the document is that it
/// outlives the daemon that wrote it.
/// </remarks>
public sealed class ReleaseStoreSpecs
{
    private static FileReleaseStore Store(TestContext context) =>
        new(context.Catalog, context.Events);

    /// <summary>
    /// A release recorded is a release read back: the same version, scheme, cards and note, and a file a person
    /// can open.
    /// </summary>
    [Fact]
    public async Task A_recorded_release_is_read_back_from_the_projects_own_file()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);

            // Nothing released yet is a state of the project, not an error.
            var before = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            Assert.Empty(before.Entries);
            Assert.Null(before.Latest);

            var first = await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest(
                    "v0.1.3",
                    ReleaseSchemes.GitReleaseId,
                    ["TASK-1", "TASK-2"],
                    "First ordinary release"),
                CancellationToken.None);

            Assert.Equal("v0.1.3", first.Version);
            Assert.Equal(ReleaseSchemes.GitReleaseId, first.SchemeId);
            Assert.Equal(new[] { "TASK-1", "TASK-2" }, first.Cards);
            Assert.Equal("First ordinary release", first.Notes);

            await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.1.4-pre", ReleaseSchemes.GitPreReleaseId),
                CancellationToken.None);

            // The history is a document beside the other documents, which is the point of it: it can be opened
            // without Aiko, and losing the database does not lose the answer to what shipped.
            var path = Path.Combine(context.StitchRoot, "releases.json");
            Assert.True(File.Exists(path), "The release history should be a document in .aiko.");
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("schemaVersion", text, StringComparison.Ordinal);
            Assert.Contains("v0.1.3", text, StringComparison.Ordinal);

            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            // Newest first, which is both how the file is written and how a history is read.
            Assert.Equal(
                new[] { "v0.1.4-pre", "v0.1.3" },
                document.Entries.Select(entry => entry.Version));
            Assert.Equal("v0.1.4-pre", document.Latest?.Version);
            // Lookup by version, and an honest nothing for a version nobody recorded.
            Assert.Equal("First ordinary release", document.Find("v0.1.3")?.Notes);
            Assert.Null(document.Find("v9.9.9"));
        });
    }

    /// <summary>
    /// A release that carried no card is recorded and read back saying exactly that.
    /// </summary>
    [Fact]
    public async Task A_release_that_carried_no_card_says_so_rather_than_nothing()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);

            var recorded = await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.2.0", ReleaseSchemes.GitReleaseId, Cards: null),
                CancellationToken.None);

            Assert.Empty(recorded.Cards);

            // An empty list is a statement about the release, so it survives the round trip as one - the file
            // holds "cards": [] rather than dropping the field.
            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            Assert.Empty(document.Latest!.Cards);
            var text = await File.ReadAllTextAsync(Path.Combine(context.StitchRoot, "releases.json"));
            Assert.Contains("\"cards\"", text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// One version has one record: recording it twice is refused, and the history is left as it was.
    /// </summary>
    [Fact]
    public async Task Recording_one_version_twice_is_refused()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.1.3", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.RecordAsync(
                    context.Project.Id,
                    new RecordReleaseRequest("v0.1.3", ReleaseSchemes.GitReleaseId, ["TASK-2"]),
                    CancellationToken.None).AsTask());

            Assert.Contains("v0.1.3", refusal.Message, StringComparison.Ordinal);

            // The refused record is not a record: the first one still stands alone, with its own cards.
            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var only = Assert.Single(document.Entries);
            Assert.Equal(new[] { "TASK-1" }, only.Cards);
        });
    }

    /// <summary>
    /// A release that names no version, or no scheme, is refused at the door and writes nothing.
    /// </summary>
    [Fact]
    public async Task A_release_that_names_no_version_or_no_scheme_is_refused_and_writes_nothing()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);

            await Assert.ThrowsAsync<ArgumentException>(() => store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("0.1.3", ReleaseSchemes.GitReleaseId),
                CancellationToken.None).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.1.3", "  "),
                CancellationToken.None).AsTask());

            // Nothing was written, so a refused release cannot be mistaken later for one that happened.
            Assert.False(File.Exists(Path.Combine(context.StitchRoot, "releases.json")));
        });
    }

    /// <summary>
    /// A document nobody can read is refused as one, rather than reported as a project that never released.
    /// </summary>
    [Fact]
    public async Task A_release_document_nobody_can_read_is_refused_rather_than_read_as_no_releases()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.1.3", ReleaseSchemes.GitReleaseId),
                CancellationToken.None);

            await File.WriteAllTextAsync(
                Path.Combine(context.StitchRoot, "releases.json"),
                "{ not a release document");

            var refused = await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.ReadAsync(context.Project.Id, CancellationToken.None).AsTask());

            // The refusal names the document it could not read, so whoever sees the error knows where to look.
            Assert.Contains("releases.json", refused.Message, StringComparison.Ordinal);
            Assert.NotNull(refused.InnerException);
        });
    }

    /// <summary>
    /// A recorded release is announced, so a screen that is open learns about it.
    /// </summary>
    [Fact]
    public async Task Recording_a_release_announces_it()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.1.3", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);

            var journal = await context.EventJournal.ReadAsync(
                context.Project.Id,
                0,
                200,
                CancellationToken.None);
            var announced = Assert.Single(
                journal,
                aikoEvent => aikoEvent.Type == AikoEventTypes.ReleasesUpdated);

            Assert.Contains("v0.1.3", announced.PayloadJson, StringComparison.Ordinal);
        });
    }
}
