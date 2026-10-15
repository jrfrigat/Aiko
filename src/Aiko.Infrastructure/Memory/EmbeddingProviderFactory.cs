using Aiko.Application.Contracts;
using Aiko.Infrastructure.Embeddings;
using Aiko.Infrastructure.Models;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Memory;

/// <summary>
/// Loads the provider that turns text into vectors for a named model, or the default one.
/// </summary>
/// <remarks>
/// An interface rather than a direct call to the ONNX provider: the search around the provider is what the
/// specs prove, and a session with a 118 MB graph is not something a spec may need, so a run substitutes a
/// deterministic provider here and the daemon the real one.
/// </remarks>
public interface IEmbeddingProviderFactory
{
    /// <summary>
    /// Returns a provider for a model, or for the default model when no id is named.
    /// </summary>
    /// <param name="modelId">Model to embed with, or null for the default.</param>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    /// <exception cref="FileNotFoundException">The model is not installed; the message says what to run.</exception>
    IEmbeddingProvider Create(string? modelId);
}

/// <summary>The real factory: the ONNX provider for a model in the registry, kept under the daemon's data.</summary>
public sealed class OnnxEmbeddingProviderFactory(
    AikoDataPaths dataPaths,
    BuiltInEmbeddingModelCatalog catalog) : IEmbeddingProviderFactory
{
    /// <summary>
    /// Length of the vectors the shipped model produces: named here because the registry records files and
    /// checksums, not tensor shapes, and the shape of the one model Aiko ships is a fact, not a guess.
    /// </summary>
    public const int DefaultDimensions = 384;

    /// <inheritdoc />
    public IEmbeddingProvider Create(string? modelId)
    {
        var id = string.IsNullOrWhiteSpace(modelId)
            ? BuiltInEmbeddingModelCatalog.MultilingualMiniLmId
            : modelId;
        var model = catalog.Find(id)
            ?? throw new KeyNotFoundException($"No embedding model is registered as '{id}'.");
        return OnnxEmbeddingProvider.Load(dataPaths, model, DefaultDimensions);
    }
}
