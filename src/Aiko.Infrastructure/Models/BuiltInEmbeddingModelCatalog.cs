using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Models;

/// <summary>
/// The embedding models this build knows, with their published checksums.
/// </summary>
/// <remarks>
/// Every value here is a fact that was read from the publisher rather than guessed: the sizes and SHA-256
/// values are the ones the model repository publishes for its files, and the licence is the one the base
/// model is released under. That matters more than it looks - a registry whose checksums were invented would
/// refuse every honest download and accept nothing, and it would be wrong in the one place a user cannot
/// check by eye.
/// <para>
/// The external addresses name a revision rather than a branch, so the bytes behind them cannot change while
/// the checksum stays the same. <see cref="EmbeddingModel.ReleaseTag"/> names the release the model's assets
/// will be published under; until that release exists, the release path refuses and says so, and the external
/// path is the one that works.
/// </para>
/// </remarks>
public sealed class BuiltInEmbeddingModelCatalog
{
    /// <summary>Revision of the model repository every external address is pinned to.</summary>
    private const string MiniLmRevision = "2c4055b12046f11709e9df2c122e59ffbdc2f900";

    /// <summary>Root of the pinned revision, under which the files below live.</summary>
    private const string MiniLmRoot =
        "https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/" + MiniLmRevision;

    /// <summary>Identifier of the model Aiko uses by default.</summary>
    public const string MultilingualMiniLmId = "paraphrase-multilingual-MiniLM-L12-v2";

    /// <summary>Creates the registry.</summary>
    public BuiltInEmbeddingModelCatalog() =>
        Models =
        [
            new EmbeddingModel(
                MultilingualMiniLmId,
                "apache-2.0",
                "models-v1",
                [
                    // The int8 graph: a quarter of the float file's size and the one worth carrying.
                    new EmbeddingModelFile(
                        "model_quantized.onnx",
                        118_308_126,
                        "66fc00f5f29afcaff34092e1bdd20008ca3918265a82fb9695a551e510cc4ebc",
                        $"{MiniLmRoot}/onnx/model_quantized.onnx"),
                    // An ONNX graph without its tokenizer cannot turn text into a vector, so it is part of the model.
                    new EmbeddingModelFile(
                        "tokenizer.json",
                        17_082_913,
                        "b60b6b43406a48bf3638526314f3d232d97058bc93472ff2de930d43686fa441",
                        $"{MiniLmRoot}/tokenizer.json")
                ])
        ];

    /// <summary>The registered models.</summary>
    public IReadOnlyList<EmbeddingModel> Models { get; }

    /// <summary>
    /// The model registered under an identifier, or null when this build does not know it.
    /// </summary>
    /// <param name="modelId">Model identifier.</param>
    public EmbeddingModel? Find(string modelId) =>
        Models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
}
