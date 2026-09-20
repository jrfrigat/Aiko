using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Moves a project's cards from the place they used to be filed to where they are filed now:
/// <c>.aiko/&lt;collection&gt;/&lt;cardId&gt;/</c> becomes <c>.aiko/workflows/&lt;collection&gt;/&lt;cardId&gt;/</c>.
/// </summary>
/// <remarks>
/// Reading already works from both places, so this is about the project converging rather than about staying
/// usable: without it a project keeps two shapes on disk for as long as nobody writes a card. It runs where
/// the other upgrades of older projects run - at daemon startup and in <c>aiko repair --fix</c> - and it is
/// idempotent, because a second run finds nothing left to move.
/// </remarks>
public static class CardLayoutMigrator
{
    /// <summary>
    /// The old-layout collections that hold cards and have not been moved yet.
    /// </summary>
    /// <remarks>
    /// Only a directory that actually holds a card is reported and moved. A directory a person made by hand
    /// is not a collection, and an empty <c>stories/</c> left by an older init is not worth moving either -
    /// the collections appear with the first card of their type.
    /// </remarks>
    public static IReadOnlyList<string> PlannedMoves(string projectRoot) =>
        FileCardStore.LegacyCollections(projectRoot)
            .Where(collection => HoldsCards(Path.Combine(AikoProjectPaths.DataRoot(projectRoot), collection)))
            .ToArray();

    /// <summary>
    /// Files those collections under <c>.aiko/workflows</c>, moving each card's whole directory.
    /// </summary>
    public static CardLayoutMigration Migrate(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var moved = new List<string>();
        var failed = new List<string>();
        foreach (var collection in PlannedMoves(projectRoot))
        {
            var source = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), collection);
            var target = Path.Combine(AikoProjectPaths.CardCollectionsRoot(projectRoot), collection);
            try
            {
                MoveCollection(source, target);
                moved.Add(collection);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A collection that cannot be moved - a read-only tree, a file held open - leaves the project
                // readable from where the cards are and is named in the report rather than failing the run.
                failed.Add(collection);
            }
        }

        return new CardLayoutMigration(moved, failed);
    }

    private static void MoveCollection(string source, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(source, target);
            return;
        }

        // The target already exists: this is a collection that is partly moved. Each card that is still in
        // the old place is moved on its own, and a card that is in both is left where the copies already are
        // - the new one wins, and the duplicate is the diagnosis' business, not the migration's.
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(directory));
            if (!Directory.Exists(destination))
            {
                Directory.Move(directory, destination);
            }
        }
    }

    private static bool HoldsCards(string collectionPath) =>
        Directory.Exists(collectionPath) &&
        Directory.EnumerateDirectories(collectionPath)
            .Any(directory => File.Exists(Path.Combine(directory, "card.json")));
}

/// <summary>What a migration did: the collections it filed the new way, and the ones it could not move.</summary>
public sealed record CardLayoutMigration(
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Failed);
