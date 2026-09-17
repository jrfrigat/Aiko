using System.Text.Json;
using Microsoft.Data.Sqlite;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Memory;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Rebuilds the SQLite projections of a project (cards, relations, memory) from the
/// file source of truth in .aiko within a single transaction.
/// </summary>
public sealed class ProjectReindexer(
    IProjectCatalog projects,
    AikoDatabase database) : IProjectReindexer
{
    /// <inheritdoc />
    public async ValueTask<ReindexResult> ReindexAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var cards = await ReadCardsAsync(project, cancellationToken);
        var relations = await ReadRelationsAsync(project, cards, cancellationToken);
        var memory = await ReadMemoryAsync(project, cancellationToken);

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await DeleteProjectionAsync(connection, transaction, "relations", project.Id, cancellationToken);
        await DeleteProjectionAsync(connection, transaction, "cards", project.Id, cancellationToken);
        await DeleteProjectionAsync(connection, transaction, "memory_fts", project.Id, cancellationToken);

        foreach (var card in cards)
        {
            await InsertCardAsync(connection, transaction, card, cancellationToken);
        }

        foreach (var relation in relations)
        {
            await InsertRelationAsync(connection, transaction, relation, cancellationToken);
        }

        foreach (var document in memory)
        {
            await InsertMemoryAsync(
                connection,
                transaction,
                project.Id,
                document,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReindexResult(cards.Count, relations.Count, memory.Count);
    }

    private static async ValueTask<IReadOnlyList<Card>> ReadCardsAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        var cards = new List<Card>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var collection in FileCardStore.Collections(project.RootPath))
        {
            await ReadCollectionAsync(collection);
        }

        return cards;

        async ValueTask ReadCollectionAsync(string collection)
        {
            var collectionPath = Path.Combine(
                AikoProjectPaths.DataRoot(project.RootPath),
                collection);
            if (!Directory.Exists(collectionPath))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(collectionPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cardPath = Path.Combine(directory, "card.json");
                if (!File.Exists(cardPath))
                {
                    continue;
                }

                await using var input = File.OpenRead(cardPath);
                var card = await JsonSerializer.DeserializeAsync(
                    input,
                    ProjectJsonContext.Default.Card,
                    cancellationToken)
                    ?? throw new InvalidDataException($"Invalid card document: {cardPath}");
                // The folder is derived from the type, so a card whose type and location disagree is a
                // corrupted project rather than a type Aiko does not know. Comparing against the derived
                // name - instead of a fixed list of collections - is what lets a project add types.
                if (!StringComparer.Ordinal.Equals(card.Reference.ProjectId, project.Id) ||
                    !StringComparer.Ordinal.Equals(
                        card.Reference.CardId,
                        Path.GetFileName(directory)) ||
                    !StringComparer.Ordinal.Equals(
                        FileCardStore.CollectionFor(card.Kind),
                        collection))
                {
                    throw new InvalidDataException(
                        $"Card identity does not match its location: {cardPath}");
                }

                if (!seenIds.Add(card.Reference.CardId))
                {
                    throw new InvalidDataException(
                        $"Card id '{card.Reference.CardId}' appears in more than one collection: {cardPath}");
                }

                cards.Add(card);
            }
        }
    }

    private static async ValueTask<IReadOnlyList<CardRelation>> ReadRelationsAsync(
        RegisteredProject project,
        IReadOnlyList<Card> cards,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "relations.json");
        if (!File.Exists(path))
        {
            return [];
        }

        await using var input = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.RelationDocument,
            cancellationToken)
            ?? throw new InvalidDataException($"Invalid relation document: {path}");
        var knownCards = cards
            .Select(card => card.Reference.CardId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var relation in document.Relations)
        {
            if (!StringComparer.Ordinal.Equals(relation.Source.ProjectId, project.Id) ||
                !knownCards.Contains(relation.Source.CardId) ||
                !knownCards.Contains(relation.Target.CardId))
            {
                throw new InvalidDataException(
                    $"Relation {relation.Id} references an unknown card.");
            }
        }

        return document.Relations;
    }

    private static async ValueTask<IReadOnlyList<MemoryDocument>> ReadMemoryAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        var memoryRoot = Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "memory");
        if (!Directory.Exists(memoryRoot))
        {
            return [];
        }

        var documents = new List<MemoryDocument>();
        foreach (var path in Directory.EnumerateFiles(
            memoryRoot,
            "*.md",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var relativePath = Path.GetRelativePath(memoryRoot, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            documents.Add(new MemoryDocument(
                relativePath,
                content,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path))));
        }

        return documents;
    }

    private static async ValueTask DeleteProjectionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask InsertCardAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Card card,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO cards(
                project_id, card_id, kind, title, stage_id, revision, document_json, updated_utc)
            VALUES (
                $projectId, $cardId, $kind, $title, $stageId, $revision, $documentJson, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$projectId", card.Reference.ProjectId);
        command.Parameters.AddWithValue("$cardId", card.Reference.CardId);
        command.Parameters.AddWithValue("$kind", card.Kind.ToString());
        command.Parameters.AddWithValue("$title", card.Title);
        command.Parameters.AddWithValue("$stageId", card.StageId);
        command.Parameters.AddWithValue("$revision", card.Revision);
        command.Parameters.AddWithValue(
            "$documentJson",
            JsonSerializer.Serialize(card, AikoJson.Project));
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask InsertRelationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CardRelation relation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO relations(
                project_id, relation_id, source_card_id, target_card_id, type, document_json, created_utc)
            VALUES (
                $projectId, $relationId, $sourceCardId, $targetCardId, $type, $documentJson, $createdUtc);
            """;
        command.Parameters.AddWithValue("$projectId", relation.Source.ProjectId);
        command.Parameters.AddWithValue("$relationId", relation.Id);
        command.Parameters.AddWithValue("$sourceCardId", relation.Source.CardId);
        command.Parameters.AddWithValue("$targetCardId", relation.Target.CardId);
        command.Parameters.AddWithValue("$type", relation.Type);
        command.Parameters.AddWithValue(
            "$documentJson",
            JsonSerializer.Serialize(relation, AikoJson.Project));
        command.Parameters.AddWithValue("$createdUtc", relation.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask InsertMemoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string projectId,
        MemoryDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO memory_fts(project_id, path, content, updated_utc)
            VALUES ($projectId, $path, $content, $updatedUtc);
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$path", document.Path);
        command.Parameters.AddWithValue("$content", document.Content);
        command.Parameters.AddWithValue("$updatedUtc", document.LastModifiedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
