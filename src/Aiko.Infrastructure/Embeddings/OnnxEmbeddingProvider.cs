using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Aiko.Infrastructure.Embeddings;

/// <summary>
/// The real provider: an ONNX graph, the tokenizer that goes with it, and the pooling that turns the graph's
/// per-token output into one vector.
/// </summary>
/// <remarks>
/// The two traps of this file are worth naming, because both fail quietly rather than loudly.
/// <para>
/// <b>Pooling.</b> A transformer's output is one vector per token, not one per text, so something has to
/// reduce them - and it has to be what the model was trained with. This model's sentence vectors are the mean
/// of its token vectors with padding left out, then normalised. Taking the classification token's vector
/// instead is a common shortcut, and it is a different number: search would still run and still return
/// results, ranked differently from how the model ranks them. That is why pooling is a named function here
/// rather than three lines inside the loop.
/// </para>
/// <para>
/// <b>Padding.</b> Every sequence in a batch is padded to the longest one, and the padded positions are not
/// text. Counting one in the mean would drag every vector that shares a batch toward whatever the graph emits
/// for padding - a number that depends on the batch rather than on the text.
/// </para>
/// </remarks>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly InferenceSession session;
    private readonly HuggingFaceTokenizer tokenizer;
    private readonly int maxTokens;
    private readonly string idsName;
    private readonly string maskName;
    private readonly string typeIdsName;
    private bool disposed;

    private OnnxEmbeddingProvider(
        string modelId,
        int dimensions,
        int maxTokens,
        string graphPath,
        string tokenizerPath)
    {
        ModelId = modelId;
        Dimensions = dimensions;
        this.maxTokens = maxTokens;
        tokenizer = HuggingFaceTokenizer.FromFile(tokenizerPath);

        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        session = new InferenceSession(graphPath, options);

        // The names the graph reads, taken from the graph itself: they belong to the ONNX file and not to
        // this build's idea of it, so looking them up is what keeps a second model from needing new code.
        idsName = FindInput("input_ids");
        maskName = FindInput("attention_mask");
        typeIdsName = FindInput("token_type_ids");
    }

    /// <inheritdoc />
    public string ModelId { get; }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <summary>
    /// Loads the provider for an installed model.
    /// </summary>
    /// <param name="dataPaths">Where the daemon's data lives; the model is kept under it.</param>
    /// <param name="model">The registered model. Its own file list says what has to be on disk.</param>
    /// <param name="dimensions">Length of the vectors the model produces.</param>
    /// <param name="maxTokens">Longest sequence to feed the graph, the two special tokens included.</param>
    /// <exception cref="FileNotFoundException">The model is not installed; the message says what to run.</exception>
    /// <exception cref="InvalidDataException">The registered model names no graph or no tokenizer.</exception>
    public static OnnxEmbeddingProvider Load(
        AikoDataPaths dataPaths,
        EmbeddingModel model,
        int dimensions,
        int maxTokens = 512)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(model);

        var directory = dataPaths.ModelDirectory(model.Id);
        var graph = model.Files.FirstOrDefault(file =>
            file.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
        var tokenizerFile = model.Files.FirstOrDefault(file =>
            string.Equals(file.Name, "tokenizer.json", StringComparison.OrdinalIgnoreCase));

        if (graph is null || tokenizerFile is null)
        {
            throw new InvalidDataException(
                $"The registered model '{model.Id}' does not name both a graph and a tokenizer, so nothing " +
                "can be embedded with it.");
        }

        var graphPath = Path.Combine(directory, graph.Name);
        var tokenizerPath = Path.Combine(directory, tokenizerFile.Name);
        if (!File.Exists(graphPath) || !File.Exists(tokenizerPath))
        {
            // The first failure a user meets, so it names the command that fixes it rather than the path.
            throw new FileNotFoundException(
                $"The embedding model '{model.Id}' is not installed. Run 'aiko model download {model.Id}' " +
                "to fetch it, or turn semantic search off.",
                graphPath);
        }

        return new OnnxEmbeddingProvider(model.Id, dimensions, maxTokens, graphPath, tokenizerPath);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (texts.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<float[]>>([]);
        }

        // Tokenise first and pad afterwards: the batch is made one length while each sequence keeps its own,
        // so a short text is not padded up to the longest one it happens to be sent with before it is even
        // counted.
        var encoded = texts.Select(text => tokenizer.Encode(text, maxTokens)).ToArray();
        var length = encoded.Max(tokens => tokens.Count);
        var batch = encoded.Length;

        var ids = new DenseTensor<long>(new[] { batch, length });
        var mask = new DenseTensor<long>(new[] { batch, length });
        var typeIds = new DenseTensor<long>(new[] { batch, length });

        for (var row = 0; row < batch; row++)
        {
            var tokens = encoded[row];
            for (var column = 0; column < length; column++)
            {
                var isToken = column < tokens.Count;
                ids[row, column] = isToken ? tokens[column] : tokenizer.PaddingId;
                mask[row, column] = isToken ? 1 : 0;
                typeIds[row, column] = 0;
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(idsName, ids),
            NamedOnnxValue.CreateFromTensor(maskName, mask),
            NamedOnnxValue.CreateFromTensor(typeIdsName, typeIds)
        };

        var vectors = new float[batch][];
        using (var outputs = session.Run(inputs))
        {
            var hidden = outputs.First().AsTensor<float>();
            if (hidden.Dimensions.Length != 3 || hidden.Dimensions[2] != Dimensions)
            {
                // A model whose vectors are a different length is a different model, and saying so is better
                // than writing vectors into an index built for another one.
                throw new InvalidDataException(
                    $"The model '{ModelId}' produced vectors of width " +
                    $"{(hidden.Dimensions.Length == 3 ? hidden.Dimensions[2] : 0)}, and {Dimensions} was expected.");
            }

            var maskValues = new long[length];
            var tokenVectors = new float[length * Dimensions];
            for (var row = 0; row < batch; row++)
            {
                for (var column = 0; column < length; column++)
                {
                    maskValues[column] = mask[row, column];
                }

                for (var token = 0; token < length; token++)
                {
                    for (var dimension = 0; dimension < Dimensions; dimension++)
                    {
                        tokenVectors[(token * Dimensions) + dimension] = hidden[row, token, dimension];
                    }
                }

                var vector = MeanPool(tokenVectors, maskValues, length, Dimensions);
                NormalizeInPlace(vector);
                vectors[row] = vector;
            }
        }

        return ValueTask.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.Dispose();
    }

    private string FindInput(string conventionalName)
    {
        foreach (var name in session.InputMetadata.Keys)
        {
            if (name.Contains(conventionalName, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return conventionalName;
    }

    /// <summary>
    /// Averages the token vectors of one sequence, ignoring the positions the attention mask marks as padding.
    /// </summary>
    /// <remarks>
    /// The mean is over the tokens that are there, which is why the mask is an argument and not an
    /// afterthought: dividing by the padded length would make every vector in a batch depend on its
    /// neighbours. A sequence with no token at all yields a zero vector, which ranks with nothing rather than
    /// with everything.
    /// </remarks>
    /// <param name="hiddenStates">The graph's output for one sequence, laid out token by token.</param>
    /// <param name="attentionMask">One per position: non-zero where the position holds a token.</param>
    /// <param name="tokenCount">Number of positions in <paramref name="hiddenStates"/>.</param>
    /// <param name="dimensions">Width of one token vector.</param>
    internal static float[] MeanPool(
        ReadOnlySpan<float> hiddenStates,
        ReadOnlySpan<long> attentionMask,
        int tokenCount,
        int dimensions)
    {
        var pooled = new float[dimensions];
        var counted = 0;

        for (var token = 0; token < tokenCount; token++)
        {
            if (attentionMask[token] == 0)
            {
                continue;
            }

            counted++;
            var offset = token * dimensions;
            for (var dimension = 0; dimension < dimensions; dimension++)
            {
                pooled[dimension] += hiddenStates[offset + dimension];
            }
        }

        if (counted == 0)
        {
            return pooled;
        }

        for (var dimension = 0; dimension < dimensions; dimension++)
        {
            pooled[dimension] /= counted;
        }

        return pooled;
    }

    /// <summary>
    /// Scales a vector to unit length, which is what turns a dot product into a cosine - and therefore what
    /// makes the ranking of a short text and a long one comparable.
    /// </summary>
    internal static void NormalizeInPlace(float[] vector)
    {
        var sumOfSquares = 0d;
        foreach (var value in vector)
        {
            sumOfSquares += (double)value * value;
        }

        var length = Math.Sqrt(sumOfSquares);
        if (length <= double.Epsilon)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }
}
