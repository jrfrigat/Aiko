using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Releases;

/// <summary>
/// File-based release history: <c>.aiko/releases.json</c> as the source of truth.
/// </summary>
/// <remarks>
/// Written the way the command queue is, for the same reasons: the document is read as a whole, so it is one
/// file rather than one per release; every change happens under a per-project lock and lands atomically, so two
/// agents recording two releases cannot lose each other's write; and Aiko keeps no second copy in SQLite,
/// because this is not a projection of anything - a rebuild of the database must not be able to lose the answer
/// to "which cards went into v0.1.3".
/// </remarks>
public sealed class FileReleaseStore(
    IProjectCatalog projects,
    IAikoEventPublisher? events = null) : IReleaseStore
{
    private const string FileName = "releases.json";
    private const int CurrentSchemaVersion = 1;

    /// <summary>Reference-counted per-project locks; idle keys are dropped automatically.</summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<ReleaseDocument> ReadAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        return await ReadDocumentAsync(project.RootPath, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ReleaseRecord> RecordAsync(
        string projectId,
        RecordReleaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A release that names no version, or a version that is not a tag, is refused before anything is
        // written: a record a reader cannot find again looks like an answer and is worse than no record.
        ReleaseRecords.Validate(request.Version, request.SchemeId);

        var project = await FindProjectAsync(projectId, cancellationToken);
        var version = request.Version.Trim();

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            // Whether the version is already recorded is decided against the history as it stands, so two
            // requests arriving together cannot both be the first record of v0.1.3.
            if (ReleaseRecords.RefuseRecord(version, document.Entries) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }

            var record = new ReleaseRecord(
                version,
                request.SchemeId.Trim(),
                DateTimeOffset.UtcNow,
                Trim(request.Notes),
                ReleaseRecords.NormalizeCards(request.Cards));

            // The new record goes first, which is what makes "newest first" a property of the stored document
            // rather than a sort somebody has to remember to do when reading.
            await WriteDocumentAsync(
                project.RootPath,
                document with { Entries = [record, .. document.Entries] },
                cancellationToken);
            await PublishAsync(project.Id, record, cancellationToken);
            return record;
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async ValueTask PublishAsync(
        string projectId,
        ReleaseRecord record,
        CancellationToken cancellationToken)
    {
        if (events is not null)
        {
            await events.PublishAsync(
                projectId,
                AikoEventTypes.ReleasesUpdated,
                JsonSerializer.Serialize(record, ReleaseJson.Document),
                cancellationToken);
        }
    }

    private static async ValueTask<ReleaseDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), FileName);
        if (!File.Exists(path))
        {
            return new ReleaseDocument(CurrentSchemaVersion, []);
        }

        try
        {
            await using var input = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ReleaseDocument>(
                       input,
                       ReleaseJson.Document,
                       cancellationToken)
                   ?? throw new InvalidDataException($"The release history at {path} holds no document.");
        }
        catch (JsonException exception)
        {
            // A document nobody can read is refused as one: reporting an empty history instead would claim the
            // project never released anything, which is a different statement from "this file is broken".
            throw new InvalidDataException(
                $"The release history at {path} cannot be read as a release document.",
                exception);
        }
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        ReleaseDocument document,
        CancellationToken cancellationToken)
    {
        var directory = AikoProjectPaths.DataRoot(projectRoot);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    document,
                    ReleaseJson.Document,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new FileNotFoundException($"No Aiko project '{projectId}' is registered.");
}
