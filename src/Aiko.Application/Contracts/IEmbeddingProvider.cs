namespace Aiko.Application.Contracts;

/// <summary>
/// Turns text into the vectors semantic search ranks by: the seam in front of the model runtime, so everything
/// else the search does - storing vectors, rebuilding them, ranking, turning the module off - is testable
/// without a model.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Identifier of the model the vectors come from, recorded beside every vector so a stale one can
    /// be told apart rather than ranked.</summary>
    string ModelId { get; }

    /// <summary>Number of components in each vector.</summary>
    int Dimensions { get; }

    /// <summary>Computes one vector per text, in the order the texts were given.</summary>
    /// <exception cref="FileNotFoundException">The model's files are not installed.</exception>
    ValueTask<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
