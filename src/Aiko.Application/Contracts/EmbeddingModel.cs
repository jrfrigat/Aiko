namespace Aiko.Application.Contracts;

/// <summary>
/// One file that makes up an embedding model, and where it can be fetched from when the release channel
/// cannot serve it.
/// </summary>
/// <param name="Name">
/// Name of the file, both in the local model directory and as the name of the release asset that carries it.
/// One name for both paths is what keeps them from drifting into two different files.
/// </param>
/// <param name="SizeBytes">Size the file must have, which is what a truncated download fails on first.</param>
/// <param name="Sha256">Lower-case hexadecimal SHA-256 of the file.</param>
/// <param name="ExternalUrl">
/// Pinned address the file can be fetched from, naming an immutable revision rather than a branch.
/// </param>
public sealed record EmbeddingModelFile(string Name, long SizeBytes, string Sha256, string ExternalUrl);

/// <summary>
/// An embedding model Aiko can use, with both places it may be fetched from.
/// </summary>
/// <remarks>
/// One expected SHA-256 per file, whoever delivers it. That is the point of the record: the release path and
/// the external path hand over the same bytes, so the registry is the single statement of what those bytes
/// are - a check that depended on which source was used would not be a check at all.
/// <para>
/// A model is a set of files rather than one file, because an ONNX graph without its tokenizer cannot turn
/// text into a vector. The set is addressed as one thing, so that "the model is present" has one meaning.
/// </para>
/// </remarks>
/// <param name="Id">Identifier Aiko addresses the model by.</param>
/// <param name="License">Licence the model is published under, so the record says what is being installed.</param>
/// <param name="ReleaseTag">
/// Release on the Aiko release channel whose assets carry these files. The tag is named here rather than
/// resolved as "latest": the files a model is made of must not change under the installation's feet.
/// </param>
/// <param name="Files">The files that make the model, verified in this order.</param>
public sealed record EmbeddingModel(
    string Id,
    string License,
    string ReleaseTag,
    IReadOnlyList<EmbeddingModelFile> Files)
{
    /// <summary>Total size of the model's files, in bytes.</summary>
    public long SizeBytes => Files.Sum(file => file.SizeBytes);
}

/// <summary>
/// Which delivery path fetches a model's files.
/// </summary>
/// <remarks>
/// Two values and not two kinds of model: the choice picks where the bytes come from, and nothing else about
/// the result - the verification is the same either way.
/// </remarks>
public enum EmbeddingModelSource
{
    /// <summary>The Aiko release channel: the same path, and the same assets, that carry an update.</summary>
    Release,

    /// <summary>The pinned third-party address recorded with the file.</summary>
    External
}

/// <summary>
/// What the installation knows about one model right now.
/// </summary>
/// <param name="Model">The model asked about.</param>
/// <param name="Present">Whether every file of it is on disk and matches its recorded checksum.</param>
/// <param name="Bytes">Bytes the model's files occupy, or zero when none of them are there.</param>
/// <param name="Detail">Human-readable line: what is present, what is missing, and why.</param>
public sealed record EmbeddingModelStatus(EmbeddingModel Model, bool Present, long Bytes, string Detail);

/// <summary>
/// Outcome of asking for a model.
/// </summary>
/// <param name="Model">The model asked for.</param>
/// <param name="Present">Whether the model is usable afterwards.</param>
/// <param name="Downloaded">Whether this call fetched anything, or the files were already there.</param>
/// <param name="Detail">Human-readable line, including the reason when the model is not present.</param>
public sealed record EmbeddingModelInstallation(
    EmbeddingModel Model,
    bool Present,
    bool Downloaded,
    string Detail);

/// <summary>
/// Fetches one file of a model into a local path. The file is written whole or not at all; verifying it
/// against the registry is the installer's job, not the downloader's.
/// </summary>
public interface IEmbeddingModelDownloader
{
    /// <summary>Which delivery path this downloader is.</summary>
    EmbeddingModelSource Source { get; }

    /// <summary>
    /// Fetches one file of a model to a local path.
    /// </summary>
    /// <param name="model">The model the file belongs to, for a source that addresses its assets by release.</param>
    /// <param name="file">The file to fetch.</param>
    /// <param name="destinationPath">Where to write it. The downloader overwrites it.</param>
    /// <param name="cancellationToken">Cancellation token for the download.</param>
    ValueTask DownloadAsync(
        EmbeddingModel model,
        EmbeddingModelFile file,
        string destinationPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Makes a model's files present in the data directory, verifies them against the registry, and says what the
/// installation has.
/// </summary>
/// <remarks>
/// Nothing is fetched until this is asked: the model is not part of the installation, and a machine that never
/// turns semantic search on must never need the network for it. A file that fails its checksum is not left
/// behind - the choice is between a model that verifies and no model, never a wrong one.
/// </remarks>
public interface IEmbeddingModelInstaller
{
    /// <summary>The models this build knows, as its registry records them.</summary>
    IReadOnlyList<EmbeddingModel> Models { get; }

    /// <summary>
    /// Reports whether a model's files are present and verified, without fetching anything.
    /// </summary>
    /// <param name="modelId">Model identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelStatus> InspectAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches whatever is missing so that the model verifies, through the named source.
    /// </summary>
    /// <param name="modelId">Model identifier.</param>
    /// <param name="source">Which delivery path to fetch through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelInstallation> EnsureAsync(
        string modelId,
        EmbeddingModelSource source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a model's files, so the space they took is the user's again.
    /// </summary>
    /// <param name="modelId">Model identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelStatus> RemoveAsync(string modelId, CancellationToken cancellationToken);
}
