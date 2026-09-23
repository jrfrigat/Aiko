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

            // A card still filed the old way moves to where cards live now, and it moves whole: its artifacts,
            // its discussion and its handoffs are inside that directory, so moving the card alone would break
            // it. When the new place is already taken the card stays where it is and is written there: writing
            // beside it would leave the card in two directories.
            var cardDirectory = existingPath is null
                ? GetCardDirectory(project.RootPath, card.Reference.CardId, card.Kind)
                : MoveLegacyCardDirectory(project.RootPath, card, existingPath);
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
    /// travels with it. A target that already exists is left alone and the card keeps its directory: the copy
    /// being written is the one the store reads, and a second copy beside it would split the card. Returns the
    /// directory the card is to be written in.
    /// </remarks>
    private static string MoveLegacyCardDirectory(string projectRoot, Card card, string existingPath)
    {
        var target = GetCardDirectory(projectRoot, card.Reference.CardId, card.Kind);
        // Resolving the target may itself have renamed the card's whole collection to the name the workflow now
        // has, carrying the card along - so the card is looked up again rather than trusted to be where it was.
        var source = FindCardDirectory(projectRoot, card.Reference.CardId)
            ?? Path.GetDirectoryName(existingPath)!;
        if (StringComparer.OrdinalIgnoreCase.Equals(source, target))
        {
            return target;
        }

        if (Directory.Exists(target))
        {
            return source;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(source, target);
        return target;
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
    /// <remarks>
    /// The type is matched against the collections that are on disk, not against the name the workflow asks
    /// for: a card filed before the naming rule changed sits under the older name, and that is exactly the
    /// card this lookup has to find.
    /// </remarks>
    private static string? GetCardPath(
        string projectRoot,
        string cardId,
        string? expectedKind)
    {
        ValidateCardId(cardId);
        var collections = expectedKind is null
            ? Collections(projectRoot)
            : ExistingCollections(projectRoot, expectedKind);

        // Every copy is looked at, not just the first one: a card that ended up in two collections must be read
        // from the same copy whatever order the directories are listed in, and the copy that saw the most writes
        // is the card. On a tie the candidate order decides - the new root before the old one.
        string? found = null;
        long foundRevision = long.MinValue;
        foreach (var (root, collection) in Candidates(projectRoot, collections))
        {
            var candidate = Path.Combine(root, collection, cardId, "card.json");
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (found is null)
            {
                found = candidate;
                foundRevision = long.MinValue;
                continue;
            }

            if (foundRevision == long.MinValue)
            {
                foundRevision = ReadRevision(found);
            }

            var revision = ReadRevision(candidate);
            if (revision > foundRevision)
            {
                found = candidate;
                foundRevision = revision;
            }
        }

        return found;
    }

    /// <summary>
    /// The revision a card document records, or -1 when it cannot be read: only used to choose between copies
    /// of one card, so an unreadable copy simply loses.
    /// </summary>
    private static long ReadRevision(string cardPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(cardPath));
            return document.RootElement.TryGetProperty("revision", out var revision) &&
                   revision.TryGetInt64(out var value)
                ? value
                : -1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return -1;
        }
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
            ResolvedCollection(projectRoot, kind),
            cardId);
    }

    /// <summary>
    /// The collection a card of this type belongs in, under the name it should carry on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A collection that is already there wins, whatever its spelling: two folders differing only in case would
    /// be one folder on Windows and two on Linux. When the spelling a project has is not the one its workflow
    /// asks for, the folder is renamed to it - that is what makes the name a person reads in the settings the
    /// name they see on disk.
    /// </para>
    /// <para>
    /// The rename goes through a temporary name because a case-only move is refused outright ("source and
    /// destination path must be different") - measured, not assumed. It is best effort: a folder that cannot be
    /// renamed is used as it is, because a rename must never fail a card's save.
    /// </para>
    /// </remarks>
    private static string ResolvedCollection(string projectRoot, string kind)
    {
        var names = CollectionNames(projectRoot, kind);
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var wanted = names[0];
        var root = AikoProjectPaths.CardCollectionsRoot(projectRoot);
        var existing = names
            .Select(name => ExistingCollection(root, name))
            .FirstOrDefault(name => name is not null);
        if (existing is null || StringComparer.Ordinal.Equals(existing, wanted))
        {
            return existing ?? wanted;
        }

        var from = Path.Combine(root, existing);
        var temporary = Path.Combine(root, $"{wanted}.aiko-rename");
        try
        {
            Directory.Move(from, temporary);
            Directory.Move(temporary, Path.Combine(root, wanted));
            return wanted;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A half-finished rename is put back, so a failure leaves one collection rather than a stray one.
            try
            {
                if (Directory.Exists(temporary) && !Directory.Exists(from))
                {
                    Directory.Move(temporary, from);
                }
            }
            catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
            {
            }

            return existing;
        }
    }

    /// <summary>The name of the collection on disk that matches <paramref name="name"/>, or null.</summary>
    private static string? ExistingCollection(string collectionsRoot, string name)
    {
        if (!Directory.Exists(collectionsRoot))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(collectionsRoot)
            .Select(directory => Path.GetFileName(directory))
            .FirstOrDefault(candidate => candidate is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(candidate, name));
    }

    /// <summary>
    /// The collections of one type as they are named on disk right now.
    /// </summary>
    /// <remarks>
    /// The names found, not the names expected: a project filed its cards under the plural before this rule and
    /// under the workflow's title after it, and on a case-sensitive filesystem those are two different
    /// directories. Matching what is actually there is what keeps every card readable.
    /// </remarks>
    private static IReadOnlyList<string> ExistingCollections(string projectRoot, string kind)
    {
        var root = AikoProjectPaths.CardCollectionsRoot(projectRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var wanted = CollectionNames(projectRoot, kind);
        return Directory
            .EnumerateDirectories(root)
            .Select(directory => Path.GetFileName(directory))
            .Where(name => name is not null && wanted.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The directory a card that exists actually sits in, or null when there is no such card. Artifacts, notes
    /// and handoffs are written beside the card, not beside where it will be.
    /// </summary>
    /// <remarks>
    /// Every collection is searched, whatever the card's type is called today: a type renamed since the card was
    /// written names another collection, and the card is still where it was. Nothing is created and nothing is
    /// moved - finding a card must never be what files it somewhere new, because a directory made for a card's
    /// notes is exactly what used to leave the card itself in two places.
    /// </remarks>
    internal static string? FindCardDirectory(string projectRoot, string cardId) =>
        GetCardPath(projectRoot, cardId, null) is { } path ? Path.GetDirectoryName(path) : null;

    /// <summary>
    /// The collection a card of this type is filed under: the title of the workflow that defines it.
    /// </summary>
    /// <param name="projectRoot">The project's root directory.</param>
    /// <param name="kind">Card type id, for example <c>Task</c>.</param>
    internal static string CollectionFor(string projectRoot, string kind) =>
        CollectionNames(projectRoot, kind) is [var name, ..] ? name : string.Empty;

    /// <summary>
    /// The names a card of this type may be filed under: the workflow's own title first, the plural of the
    /// type's id second.
    /// </summary>
    /// <remarks>
    /// The title is the name a person reads in the project settings, so a folder is called what the type is
    /// called there. The plural stays in the list because every project made before this rule filed its cards
    /// that way: looking under both names is what lets an existing project open without a migration, and what
    /// keeps a card readable after its workflow has been renamed.
    /// </remarks>
    /// <param name="projectRoot">The project's root directory.</param>
    /// <param name="kind">Card type id, for example <c>Task</c>.</param>
    internal static IReadOnlyList<string> CollectionNames(string projectRoot, string kind)
    {
        var id = CardKind.ToWorkflowId(kind ?? string.Empty);
        var names = new List<string>();
        var fromTitle = SanitizeCollectionName(
            FileProjectDefinitionStore.ReadWorkflowTitle(projectRoot, id));
        if (fromTitle.Length > 0)
        {
            names.Add(fromTitle);
        }

        var plural = Pluralize(id);
        if (plural.Length > 0 && !names.Contains(plural, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(plural);
        }

        return names;
    }

    /// <summary>
    /// A workflow title as a directory name: the characters a path cannot carry become dashes, and surrounding
    /// whitespace and dots are dropped.
    /// </summary>
    /// <remarks>
    /// A title is free text from the settings - it may hold a slash, a colon or nothing but spaces. The name is
    /// still the project's own and is not translated; it is only made fit to be a folder.
    /// </remarks>
    internal static string SanitizeCollectionName(string? title)
    {
        if (title is null)
        {
            return string.Empty;
        }

        var characters = title
            .Select(character => character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                || char.IsControl(character)
                    ? '-'
                    : character)
            .ToArray();
        return new string(characters).Trim().Trim('.');
    }

    /// <summary>
    /// The plural of a type id by the ordinary English rule: a consonant before a final <c>y</c> becomes
    /// <c>ies</c>, a final <c>s</c>, <c>x</c>, <c>z</c>, <c>ch</c> or <c>sh</c> takes <c>es</c>, and everything
    /// else takes <c>s</c>. The old hand-written list existed for <c>story</c>; it is just the rule applied to
    /// a consonant before a <c>y</c>.
    /// </summary>
    /// <remarks>
    /// This is no longer the name cards are filed under - the workflow's title is - but it is still the name
    /// of every collection written before that rule, and the name a type falls back to when the project holds
    /// no workflow for it.
    /// </remarks>
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
