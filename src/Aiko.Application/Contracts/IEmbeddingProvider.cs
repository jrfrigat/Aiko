namespace Aiko.Application.Contracts;

/// <summary>
/// Turns text into the vectors semantic search ranks by.
/// </summary>
/// <remarks>
/// The seam in front of the model runtime, and it is here on purpose. Everything else the search does -
/// storing vectors, rebuilding them, deciding a stale entry, ranking, turning the module off - is testable
/// without a model, and it could not be tested at all if the calling code knew about an ONNX session: a
/// native library and a 118 MB file would have to be present for any of it to run.
/// <para>
/// Which is also why this is an interface rather than a class: a run of specs proves the search around the
/// provider, and answers about the vectors themselves - whether the pooling matches what the model was
/// trained with - are a manual comparison against a reference vector. One is what a machine can hold; the
/// other is what a person has to look at, and confusing the two would put a green check on an unchecked thing.
/// </para>
/// </remarks>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Identifier of the model the vectors come from. Recorded beside every vector, so that a vector
    /// computed by a different model can be told apart from a current one instead of being ranked.
    /// </summary>
    string ModelId { get; }

    /// <summary>Number of components in each vector. A model's vectors are all this long.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Computes one vector per text.
    /// </summary>
    /// <remarks>
    /// A batch rather than a single text: the graph runs over a batch anyway, and a caller that embeds a
    /// document and its sections should not pay for a session call per string.
    /// </remarks>
    /// <param name="texts">Texts to embed, in the order the vectors are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One vector per text, in the same order as <paramref name="texts"/>.</returns>
    /// <exception cref="FileNotFoundException">
    /// The model's files are not installed. The message names the model and what to run, because this is the
    /// failure a user meets first.
    /// </exception>
    ValueTask<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
