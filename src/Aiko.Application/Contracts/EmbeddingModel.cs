namespace Aiko.Application.Contracts;

/// <summary>
/// One file that makes up an embedding model, and where it can be fetched from when the release channel
/// cannot serve it: <c>ExternalUrl</c> names an immutable revision rather than a branch, and the size is what
/// a truncated download fails on first.
/// </summary>
public sealed record EmbeddingModelFile(string Name, long SizeBytes, string Sha256, string ExternalUrl);

/// <summary>
/// An embedding model Aiko can use, with both places it may be fetched from. A set of files rather than one,
/// because an ONNX graph without its tokenizer cannot turn text into a vector, addressed as one thing so that
/// "the model is present" has one meaning.
/// </summary>
/// <remarks>
/// One expected SHA-256 per file, whoever delivers it - the registry is the single statement of what those
/// bytes are - and <c>ReleaseTag</c> is named rather than resolved as "latest", so the files a model is made
/// of cannot change under the installation's feet.
/// </remarks>
public sealed record EmbeddingModel(
    string Id,
    string License,
    string ReleaseTag,
    IReadOnlyList<EmbeddingModelFile> Files)
{
    public long SizeBytes => Files.Sum(file => file.SizeBytes);
}

/// <summary>
/// Which delivery path fetches a model's files. Two values and not two kinds of model: the choice picks where
/// the bytes come from and nothing else about the result - the verification is the same either way.
/// </summary>
public enum EmbeddingModelSource
{
    /// <summary>The Aiko release channel: the same path, and the same assets, that carry an update.</summary>
    Release,

    /// <summary>The pinned third-party address recorded with the file.</summary>
    External
}

/// <summary>
/// What the installation knows about one model right now: <c>Present</c> means every file is on disk and
/// matches its recorded checksum, <c>Bytes</c> is what they occupy (zero when none is there), and
/// <c>Detail</c> is a human-readable line saying what is present, what is missing and why.
/// </summary>
public sealed record EmbeddingModelStatus(EmbeddingModel Model, bool Present, long Bytes, string Detail);

/// <summary>
/// Outcome of asking for a model: <c>Present</c> says the model is usable afterwards, <c>Downloaded</c>
/// whether this call fetched anything or the files were already there, and <c>Detail</c> is a human-readable
/// line including the reason when it is not present.
/// </summary>
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

    /// <summary>Fetches one file of a model to a local path, overwriting whatever is there.</summary>
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

    /// <summary>Reports whether a model's files are present and verified, without fetching anything.</summary>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelStatus> InspectAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>Fetches whatever is missing so that the model verifies, through the named source.</summary>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelInstallation> EnsureAsync(
        string modelId,
        EmbeddingModelSource source,
        CancellationToken cancellationToken);

    /// <summary>Deletes a model's files, so the space they took is the user's again.</summary>
    /// <exception cref="KeyNotFoundException">No model is registered under that identifier.</exception>
    ValueTask<EmbeddingModelStatus> RemoveAsync(string modelId, CancellationToken cancellationToken);
}
