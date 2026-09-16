using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Cards;

/// <summary>
/// File-based card store: card.json in the card directory as the source of truth,
/// atomic writes with optimistic revisions, mirroring into the SQLite projection and
/// a card.updated event for every successful save.
/// </summary>
public sealed class FileCardStore(
    IProjectCatalog projects,
    AikoDatabase database,
    IAikoEventPublisher? events = null) : ICardStore
{
    /// <summary>
    /// Reference-counted per-card locks; idle keys are dropped automatically.
    /// </summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Card>> ListAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT document_json
            FROM cards
            WHERE project_id = $projectId
            ORDER BY card_id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        var cards = new List<Card>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var card = JsonSerializer.Deserialize(
                reader.GetString(0),
                ProjectJsonContext.Default.Card);
            if (card is not null)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    /// <inheritdoc />
    public async ValueTask<Card?> FindAsync(
        CardReference reference,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(reference.ProjectId, cancellationToken);
        var cardPath = GetCardPath(project.RootPath, reference.CardId, null);
        if (cardPath is null)
        {
            return null;
        }

        await using var input = File.OpenRead(cardPath);
        return await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.Card,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        Card card,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ValidateCardId(card.Reference.CardId);

        if (card.Revision != expectedRevision + 1)
        {
            throw new ArgumentException(
                $"Saved card revision must be {expectedRevision + 1}.",
                nameof(card));
        }

        var project = await FindProjectAsync(card.Reference.ProjectId, cancellationToken);
        var lockKey = $"{project.Id}/{card.Reference.CardId}";

        using (await locks.LockAsync(lockKey, cancellationToken))
        {
            var existingPath = GetCardPath(project.RootPath, card.Reference.CardId, null);
            var actualRevision = existingPath is null
                ? 0
                : (await ReadCardAsync(existingPath, cancellationToken)).Revision;
            if (actualRevision != expectedRevision)
            {
                throw new RevisionConflictException(
                    $"{project.Id}/{card.Reference.CardId}",
                    expectedRevision,
                    actualRevision);
            }

            var cardDirectory = GetCardDirectory(
                project.RootPath,
                card.Reference.CardId,
                card.Kind);
            Directory.CreateDirectory(cardDirectory);
            var cardPath = Path.Combine(cardDirectory, "card.json");
            await WriteCardAtomicallyAsync(cardPath, card, cancellationToken);
            await UpsertProjectionAsync(card, cancellationToken);
            if (events is not null)
            {
                await events.PublishAsync(
                    card.Reference.ProjectId,
                    AikoEventTypes.CardUpdated,
                    JsonSerializer.Serialize(card, ProjectJsonContext.Default.Card),
                    cancellationToken);
            }
        }
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    private static string? GetCardPath(
        string projectRoot,
        string cardId,
        CardKind? expectedKind)
    {
        ValidateCardId(cardId);
        var kinds = expectedKind is null
            ? new[] { CardKind.Story, CardKind.Task }
            : new[] { expectedKind.Value };

        foreach (var kind in kinds)
        {
            var candidate = Path.Combine(
                GetCardDirectory(projectRoot, cardId, kind),
                "card.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the card directory inside the stories or tasks collection.
    /// </summary>
    internal static string GetCardDirectory(
        string projectRoot,
        string cardId,
        CardKind kind)
    {
        ValidateCardId(cardId);
        var collection = kind == CardKind.Story ? "stories" : "tasks";
        return Path.Combine(AikoProjectPaths.DataRoot(projectRoot), collection, cardId);
    }

    /// <summary>
    /// Verifies that a card identifier is safe as a directory/file name on every
    /// supported platform (ASCII, no reserved Windows names).
    /// </summary>
    internal static void ValidateCardId(string cardId) =>
        FileSystemSafeIdentifiers.Validate(cardId, "card");

    private static async ValueTask<Card> ReadCardAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.Card,
            cancellationToken)
            ?? throw new InvalidDataException($"Invalid card document: {path}");
    }

    private static async ValueTask WriteCardAtomicallyAsync(
        string cardPath,
        Card card,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{cardPath}.{Guid.NewGuid():N}.tmp";
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
                    card,
                    ProjectJsonContext.Default.Card,
                    cancellationToken);
            }

            File.Move(temporaryPath, cardPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async ValueTask UpsertProjectionAsync(
        Card card,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(card, ProjectJsonContext.Default.Card);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cards(
                project_id,
                card_id,
                kind,
                title,
                stage_id,
                revision,
                document_json,
                updated_utc)
            VALUES (
                $projectId,
                $cardId,
                $kind,
                $title,
                $stageId,
                $revision,
                $documentJson,
                $updatedUtc)
            ON CONFLICT(project_id, card_id) DO UPDATE SET
                kind = excluded.kind,
                title = excluded.title,
                stage_id = excluded.stage_id,
                revision = excluded.revision,
                document_json = excluded.document_json,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$projectId", card.Reference.ProjectId);
        command.Parameters.AddWithValue("$cardId", card.Reference.CardId);
        command.Parameters.AddWithValue("$kind", card.Kind.ToString());
        command.Parameters.AddWithValue("$title", card.Title);
        command.Parameters.AddWithValue("$stageId", card.StageId);
        command.Parameters.AddWithValue("$revision", card.Revision);
        command.Parameters.AddWithValue("$documentJson", json);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
