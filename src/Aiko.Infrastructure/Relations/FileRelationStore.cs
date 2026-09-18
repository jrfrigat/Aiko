using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Aiko.Infrastructure.Threading;

namespace Aiko.Infrastructure.Relations;

/// <summary>
/// File-based store of card relations: relations.json as the source of truth
/// with blocks-cycle validation, mirroring into the SQLite projection and
/// a relations.updated event for every change.
/// </summary>
public sealed class FileRelationStore(
    IProjectCatalog projects,
    ICardStore cards,
    AikoDatabase database,
    IAikoEventPublisher? events = null) : IRelationStore
{
    /// <summary>
    /// Reference-counted per-project locks; idle keys are dropped automatically.
    /// </summary>
    private readonly KeyedLockStore locks = new();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CardRelation>> ListAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindProjectAsync(projectId, cancellationToken);
        return (await ReadDocumentAsync(project.RootPath, cancellationToken)).Relations;
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(CardRelation relation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var project = await FindProjectAsync(relation.Source.ProjectId, cancellationToken);
        // What goes into the document is the project's immutable id, however the caller addressed the project.
        // An agent's MCP endpoint carries the readable handle, so a relation built from the route value would
        // be filed under the handle - and the reindexer refuses the whole project over one relation it cannot
        // resolve against its cards, which is how a single edge froze every projection.
        relation = WithProjectId(relation, project.Id);
        await EnsureCardExistsAsync(relation.Source, cancellationToken);
        await EnsureCardExistsAsync(relation.Target, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var relations = document.Relations
                .Where(item => !StringComparer.Ordinal.Equals(item.Id, relation.Id))
                .ToList();

            if (IsDuplicateSymmetricRelation(relations, relation))
            {
                throw new InvalidOperationException(
                    "The same relates-to edge is already stored in the opposite direction.");
            }

            relations.Add(relation);
            EnsureBlocksAcyclic(relations);

            var updated = document with
            {
                Revision = document.Revision + 1,
                Relations = relations
            };
            await WriteDocumentAsync(project.RootPath, updated, cancellationToken);
            await ReplaceProjectionAsync(project.Id, relations, cancellationToken);
            if (events is not null)
            {
                await events.PublishAsync(
                    project.Id,
                    AikoEventTypes.RelationsUpdated,
                    string.Empty,
                    cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(
        string projectId,
        string relationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationId);
        var project = await FindProjectAsync(projectId, cancellationToken);

        using (await locks.LockAsync(project.Id, cancellationToken))
        {
            var document = await ReadDocumentAsync(project.RootPath, cancellationToken);
            var relations = document.Relations
                .Where(item => !StringComparer.Ordinal.Equals(item.Id, relationId))
                .ToList();
            if (relations.Count == document.Relations.Count)
            {
                return;
            }

            var updated = document with
            {
                Revision = document.Revision + 1,
                Relations = relations
            };
            await WriteDocumentAsync(project.RootPath, updated, cancellationToken);
            await ReplaceProjectionAsync(project.Id, relations, cancellationToken);
            if (events is not null)
            {
                await events.PublishAsync(
                    project.Id,
                    AikoEventTypes.RelationsUpdated,
                    string.Empty,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// The relation with both references filed under <paramref name="projectId"/>: the file holds one project's
    /// edges, and every reference in it has to be the id the cards and the projections are keyed by.
    /// </summary>
    /// <remarks>
    /// A fresh relation rather than <c>with</c>, because <see cref="CardReference"/> exposes its parts as
    /// read-only: a reference names a card, and rebuilding it is the only way to change what it names.
    /// </remarks>
    private static CardRelation WithProjectId(CardRelation relation, string projectId) =>
        new(
            relation.Id,
            new CardReference(projectId, relation.Source.CardId),
            new CardReference(projectId, relation.Target.CardId),
            relation.Type,
            relation.CreatedAt);

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    private async ValueTask EnsureCardExistsAsync(
        CardReference reference,
        CancellationToken cancellationToken)
    {
        if (await cards.FindAsync(reference, cancellationToken) is null)
        {
            throw new KeyNotFoundException(
                $"Cannot create a relation to unknown card: {reference.ProjectId}/{reference.CardId}");
        }
    }

    private static bool IsDuplicateSymmetricRelation(
        IReadOnlyList<CardRelation> relations,
        CardRelation candidate) =>
        candidate.IsSymmetric &&
        relations.Any(existing =>
            existing.IsSymmetric &&
            existing.Source == candidate.Target &&
            existing.Target == candidate.Source);

    private static void EnsureBlocksAcyclic(IReadOnlyList<CardRelation> relations)
    {
        var edges = relations
            .Where(relation => StringComparer.Ordinal.Equals(relation.Type, RelationTypes.Blocks))
            .GroupBy(relation => relation.Source.CardId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(relation => relation.Target.CardId).ToArray(),
                StringComparer.Ordinal);
        var states = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in edges.Keys)
        {
            Visit(node);
        }

        void Visit(string node)
        {
            if (states.TryGetValue(node, out var state))
            {
                if (state == 1)
                {
                    throw new InvalidOperationException("A blocks relation cannot create a cycle.");
                }

                return;
            }

            states[node] = 1;
            if (edges.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    Visit(target);
                }
            }

            states[node] = 2;
        }
    }

    private static async ValueTask<RelationDocument> ReadDocumentAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "relations.json");
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            input,
            ProjectJsonContext.Default.RelationDocument,
            cancellationToken)
            ?? throw new InvalidDataException($"Invalid relation document: {path}");
    }

    private static async ValueTask WriteDocumentAsync(
        string projectRoot,
        RelationDocument document,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "relations.json");
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
                    AikoJson.Project,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async ValueTask ReplaceProjectionAsync(
        string projectId,
        IReadOnlyList<CardRelation> relations,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM relations WHERE project_id = $projectId;";
            delete.Parameters.AddWithValue("$projectId", projectId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var relation in relations)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO relations(
                    project_id,
                    relation_id,
                    source_card_id,
                    target_card_id,
                    type,
                    document_json,
                    created_utc)
                VALUES (
                    $projectId,
                    $relationId,
                    $sourceCardId,
                    $targetCardId,
                    $type,
                    $documentJson,
                    $createdUtc);
                """;
            insert.Parameters.AddWithValue("$projectId", projectId);
            insert.Parameters.AddWithValue("$relationId", relation.Id);
            insert.Parameters.AddWithValue("$sourceCardId", relation.Source.CardId);
            insert.Parameters.AddWithValue("$targetCardId", relation.Target.CardId);
            insert.Parameters.AddWithValue("$type", relation.Type);
            insert.Parameters.AddWithValue(
                "$documentJson",
                JsonSerializer.Serialize(relation, AikoJson.Project));
            insert.Parameters.AddWithValue("$createdUtc", relation.CreatedAt.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
