using System.Text.Json;
using Aiko.Application.Contracts;
using Microsoft.Data.Sqlite;
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
    IAikoEventPublisher? events = null,
    IProjectAnalytics? analytics = null) : ICardStore
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

        // Cards are projected under the project's immutable id, but a caller may address the project by its
        // readable handle - the UI's URLs do, and so do the MCP routes. Resolving here is what keeps the
        // board from answering with an empty list for a project that plainly has cards.
        var project = await FindProjectAsync(projectId, cancellationToken);

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
        command.Parameters.AddWithValue("$projectId", project.Id);

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
        // The caller may have addressed the project by its readable handle; what is stored, projected and
        // published is always keyed by the immutable id, so a card can never be filed under a handle.
        card = card with { Reference = new CardReference(project.Id, card.Reference.CardId) };
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
            // A card still filed the old way moves to where cards live now, and it moves whole: its artifacts,
            // its discussion and its handoffs are inside that directory, so moving the card alone would break
            // it. Skipped when the new place already holds this card - there the new copy is the one this
            // write is about, and the leftover in the old place is for the diagnosis to name.
            MoveLegacyCardDirectory(project.RootPath, card, existingPath);
            Directory.CreateDirectory(cardDirectory);
            var cardPath = Path.Combine(cardDirectory, "card.json");
            await WriteCardAtomicallyAsync(cardPath, card, cancellationToken);
            await UpsertProjectionAsync(card, cancellationToken);
            if (events is not null)
            {
                await events.PublishAsync(
                    card.Reference.ProjectId,
                    AikoEventTypes.CardUpdated,
                    JsonSerializer.Serialize(card, AikoJson.Project),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Files a card under <c>.aiko/workflows</c> when it still sits in the project's own data root.
    /// </summary>
    /// <remarks>
    /// The whole card directory is moved rather than the document copied, so everything filed beside the card
    /// travels with it. A target that already exists is left alone: that means the card is in both places,
    /// and the copy in the new place is the one being written.
    /// </remarks>
    private static void MoveLegacyCardDirectory(string projectRoot, Card card, string? existingPath)
    {
        if (existingPath is null)
        {
            return;
        }

        var source = Path.GetDirectoryName(existingPath)!;
        var target = GetCardDirectory(projectRoot, card.Reference.CardId, card.Kind);
        if (StringComparer.OrdinalIgnoreCase.Equals(source, target) || Directory.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(source, target);
    }

    private async ValueTask<RegisteredProject> FindProjectAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

    /// <summary>
    /// The card document, wherever the card is filed: under <c>.aiko/workflows</c> first, and under the
    /// project's own root second, because a project made before cards moved there is still a project with
    /// cards. The new place wins when a card is somehow in both: that is where cards are written now.
    /// </summary>
    private static string? GetCardPath(
        string projectRoot,
        string cardId,
        string? expectedKind)
    {
        ValidateCardId(cardId);
        var collections = expectedKind is null
            ? Collections(projectRoot)
            : [CollectionFor(expectedKind)];

        foreach (var (root, collection) in Candidates(projectRoot, collections))
        {
            var candidate = Path.Combine(root, collection, cardId, "card.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The two places a card can be: the collection root cards live in now, and the project root they used
    /// to live in. Every new-root candidate is offered before any old-root one, so the new place always wins
    /// when a card is in both.
    /// </summary>
    /// <remarks>
    /// The old root is searched under every collection it has, not only under the ones the new root lacks:
    /// a caller that knows the card's type hands over exactly one collection name, and filtering the old
    /// root by that list would mean never looking where the card actually is.
    /// </remarks>
    private static IEnumerable<(string Root, string Collection)> Candidates(
        string projectRoot,
        IReadOnlyList<string> collections)
    {
        foreach (var collection in collections)
        {
            yield return (AikoProjectPaths.CardCollectionsRoot(projectRoot), collection);
        }

        foreach (var collection in LegacyCollections(projectRoot))
        {
            yield return (AikoProjectPaths.DataRoot(projectRoot), collection);
        }
    }

    /// <summary>
    /// Returns the card directory inside the collection of its type, where cards are filed today.
    /// </summary>
    internal static string GetCardDirectory(
        string projectRoot,
        string cardId,
        string kind)
    {
        ValidateCardId(cardId);
        return Path.Combine(
            AikoProjectPaths.CardCollectionsRoot(projectRoot),
            CollectionFor(kind),
            cardId);
    }

    /// <summary>
    /// The directory a card that already exists actually sits in - the old place for a project that has not
    /// been migrated yet. Artifacts and notes are written beside the card, not beside where it will be.
    /// </summary>
    internal static string GetExistingCardDirectory(
        string projectRoot,
        string cardId,
        string kind)
    {
        var path = GetCardPath(projectRoot, cardId, kind);
        return path is null
            ? GetCardDirectory(projectRoot, cardId, kind)
            : Path.GetDirectoryName(path)!;
    }

    /// <summary>
    /// The collection a card type is filed under: the plural of the type's own id by the ordinary English
    /// rule, so <c>Story</c> lives under <c>stories</c>, <c>Epic</c> under <c>epics</c> and a type the engine
    /// never heard of - <c>Bug</c> - under <c>bugs</c>. No type name is special-cased, and the folders projects
    /// already have on disk keep the names this rule gives them.
    /// </summary>
    /// <param name="kind">Card type id, for example <c>Story</c>.</param>
    internal static string CollectionFor(string kind) =>
        Pluralize(kind?.Trim().ToLowerInvariant() ?? string.Empty);

    /// <summary>
    /// The plural of a type id by the ordinary English rule: a consonant before a final <c>y</c> becomes
    /// <c>ies</c>, a final <c>s</c>, <c>x</c>, <c>z</c>, <c>ch</c> or <c>sh</c> takes <c>es</c>, and everything
    /// else takes <c>s</c>. The old hand-written list existed for <c>story</c>; it is just the rule applied to
    /// a consonant before a <c>y</c>.
    /// </summary>
    /// <param name="id">Lower-cased type id.</param>
    internal static string Pluralize(string id)
    {
        if (id.Length == 0)
        {
            return id;
        }

        if (id.EndsWith('y') && id.Length >= 2 && IsConsonant(id[^2]))
        {
            return string.Concat(id.AsSpan(0, id.Length - 1), "ies");
        }

        if (id.EndsWith("ch", StringComparison.Ordinal) ||
            id.EndsWith("sh", StringComparison.Ordinal) ||
            id.EndsWith('s') ||
            id.EndsWith('x') ||
            id.EndsWith('z'))
        {
            return id + "es";
        }

        return id + "s";
    }

    private static bool IsConsonant(char value) =>
        char.IsLetter(value) && value is not ('a' or 'e' or 'i' or 'o' or 'u');

    /// <summary>
    /// The card collections a project has: every directory below <c>.aiko/workflows</c>. Derived from disk
    /// rather than from a fixed list, because the set of card types is project data - and no longer carrying
    /// a list of service directories to steer around, since a card lives nowhere near them any more. What
    /// keeps the two apart is the shape of the entry: a directory under <c>workflows</c> is a collection, a
    /// <c>.json</c> file there is a workflow definition.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project.</param>
    internal static IReadOnlyList<string> Collections(string projectRoot) =>
        Directories(AikoProjectPaths.CardCollectionsRoot(projectRoot), reserved: []);

    /// <summary>
    /// The collections of a project made before cards moved under <c>.aiko/workflows</c>: every directory in
    /// the data root that is not a service one. Read so that such a project keeps working until it is
    /// migrated, and used by the migration that files its cards the new way.
    /// </summary>
    internal static IReadOnlyList<string> LegacyCollections(string projectRoot) =>
        Directories(AikoProjectPaths.DataRoot(projectRoot), ServiceDirectories);

    /// <summary>Directories below <c>.aiko</c> that hold something other than cards.</summary>
    internal static readonly string[] ServiceDirectories =
        [AikoProjectPaths.WorkflowsDirectoryName, "projections", "memory", "runtime", "handoffs"];

    private static IReadOnlyList<string> Directories(string root, string[] reserved)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 } &&
                           !reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .ToArray();
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
                    AikoJson.Project,
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

    /// <summary>
    /// The stage the card store currently has projected for this card, or null when it has none - which is
    /// how a card that did not exist until this save is told apart from one that moved.
    /// </summary>
    private static async ValueTask<string?> ReadProjectedStageAsync(
        SqliteConnection connection,
        Card card,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stage_id FROM cards WHERE project_id = $projectId AND card_id = $cardId";
        command.Parameters.AddWithValue("$projectId", card.Reference.ProjectId);
        command.Parameters.AddWithValue("$cardId", card.Reference.CardId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async ValueTask UpsertProjectionAsync(
        Card card,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(card, AikoJson.Project);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // The stage a card was in before this save is what makes a transition worth recording: an edit that
        // leaves the stage alone is not a move and does not belong on the throughput chart.
        if (analytics is not null)
        {
            var previous = await ReadProjectedStageAsync(connection, card, cancellationToken);
            if (previous is null)
            {
                await analytics.RecordStageAsync(
                    card.Reference.ProjectId,
                    card.Reference.CardId,
                    fromStageId: null,
                    card.StageId,
                    card.Kind,
                    cancellationToken);
            }
            else if (!string.Equals(previous, card.StageId, StringComparison.Ordinal))
            {
                await analytics.RecordStageAsync(
                    card.Reference.ProjectId,
                    card.Reference.CardId,
                    previous,
                    card.StageId,
                    card.Kind,
                    cancellationToken);
            }
        }

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
        command.Parameters.AddWithValue("$kind", card.Kind);
        command.Parameters.AddWithValue("$title", card.Title);
        command.Parameters.AddWithValue("$stageId", card.StageId);
        command.Parameters.AddWithValue("$revision", card.Revision);
        command.Parameters.AddWithValue("$documentJson", json);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
