namespace Aiko.Application.Contracts;

/// <summary>
/// Rebuilds the SQLite projections from the file source of truth (.aiko).
/// </summary>
public interface IProjectReindexer
{
    /// <summary>
    /// Fully rebuilds the card, relation and memory projections of the project from files
    /// and returns the number of reindexed records of each kind.
    /// </summary>
    ValueTask<ReindexResult> ReindexAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Summary of a project reindex: how many cards, relations and memory documents were rebuilt.
/// </summary>
public sealed record ReindexResult(int Cards, int Relations, int MemoryDocuments);
