using Aiko.Domain.Cards;

namespace Aiko.Application.Contracts;

/// <summary>
/// Store of card Markdown artifacts; paths are confined to the card directory.
/// </summary>
public interface ICardArtifactStore
{
    /// <summary>
    /// Lists all Markdown artifacts of the card with relative paths.
    /// </summary>
    ValueTask<IReadOnlyList<CardArtifactSummary>> ListAsync(
        CardReference card,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads an artifact by relative path or returns null.
    /// </summary>
    ValueTask<CardArtifactDocument?> ReadAsync(
        CardReference card,
        string path,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves an artifact with an optimistic version check against the current
    /// <see cref="CardArtifactDocument.Version"/>.
    /// </summary>
    ValueTask<CardArtifactDocument> SaveAsync(
        CardReference card,
        string path,
        string content,
        string? expectedVersion,
        CancellationToken cancellationToken);
}
