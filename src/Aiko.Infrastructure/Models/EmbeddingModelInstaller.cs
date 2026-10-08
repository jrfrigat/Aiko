using System.Globalization;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Installation;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Models;

/// <summary>
/// Keeps the installation's copy of an embedding model honest: present and verified, or absent.
/// </summary>
/// <remarks>
/// The two delivery paths are held to one rule here. Whichever source fetched a file, it is checked against the
/// registry's size and SHA-256 before it is put in place, so "the model is installed" means the same thing on
/// both - and a source that served something else produces no model rather than a wrong one.
/// <para>
/// Nothing is fetched until <see cref="EnsureAsync"/> asks for it. The model is not part of the installation,
/// so a machine that never turns semantic search on never needs the network for it, and the space it would take
/// stays the user's until they ask.
/// </para>
/// </remarks>
public sealed class EmbeddingModelInstaller : IEmbeddingModelInstaller
{
    private readonly AikoDataPaths paths;
    private readonly IReadOnlyList<EmbeddingModel> models;
    private readonly IReadOnlyDictionary<EmbeddingModelSource, IEmbeddingModelDownloader> downloaders;

    /// <summary>Creates the installer.</summary>
    /// <param name="dataPaths">Where the daemon's data lives; the model is kept under it.</param>
    /// <param name="models">The registry of models this build knows.</param>
    /// <param name="downloaders">One downloader per delivery path.</param>
    /// <exception cref="ArgumentException">Two downloaders claim the same source.</exception>
    public EmbeddingModelInstaller(
        AikoDataPaths dataPaths,
        IReadOnlyList<EmbeddingModel> models,
        IEnumerable<IEmbeddingModelDownloader> downloaders)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(downloaders);

        paths = dataPaths;
        this.models = models;

        // Two downloaders for one source would make "where did this come from" depend on registration order,
        // so it is refused rather than resolved.
        this.downloaders = downloaders.ToDictionary(downloader => downloader.Source);
    }

    /// <inheritdoc />
    public IReadOnlyList<EmbeddingModel> Models => models;

    /// <inheritdoc />
    public async ValueTask<EmbeddingModelStatus> InspectAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        var model = Find(modelId);
        var problems = new List<string>();
        var bytes = 0L;

        foreach (var file in model.Files)
        {
            if (await ProblemAsync(PathOf(model, file), file, cancellationToken) is string problem)
            {
                problems.Add($"{file.Name}: {problem}");
            }
            else
            {
                bytes += file.SizeBytes;
            }
        }

        return problems.Count == 0
            ? new EmbeddingModelStatus(
                model,
                Present: true,
                bytes,
                $"Installed: {model.Files.Count} file(s), {Megabytes(model.SizeBytes)} MB, checksums match.")
            : new EmbeddingModelStatus(
                model,
                Present: false,
                bytes,
                $"Not installed - {string.Join("; ", problems)}.");
    }

    /// <inheritdoc />
    public async ValueTask<EmbeddingModelInstallation> EnsureAsync(
        string modelId,
        EmbeddingModelSource source,
        CancellationToken cancellationToken)
    {
        var model = Find(modelId);
        if (!downloaders.TryGetValue(source, out var downloader))
        {
            return new EmbeddingModelInstallation(
                model,
                Present: false,
                Downloaded: false,
                $"This build has no downloader for the '{source}' source.");
        }

        var missing = new List<EmbeddingModelFile>();
        foreach (var file in model.Files)
        {
            if (await ProblemAsync(PathOf(model, file), file, cancellationToken) is not null)
            {
                missing.Add(file);
            }
        }

        if (missing.Count == 0)
        {
            return new EmbeddingModelInstallation(
                model,
                Present: true,
                Downloaded: false,
                $"Already installed: {model.Files.Count} file(s), {Megabytes(model.SizeBytes)} MB, " +
                "checksums match - nothing was downloaded.");
        }

        Directory.CreateDirectory(paths.ModelDirectory(model.Id));
        var fetched = 0;
        try
        {
            foreach (var file in missing)
            {
                var target = PathOf(model, file);

                // Downloaded beside its destination and verified where it lies: a file that fails its checksum
                // never reaches the path a later run would read as the model.
                var partial = target + ".part";
                try
                {
                    await downloader.DownloadAsync(model, file, partial, cancellationToken);
                    if (await ProblemAsync(partial, file, cancellationToken) is string problem)
                    {
                        RemoveDirectoryIfEmpty(model);
                        return new EmbeddingModelInstallation(
                            model,
                            Present: false,
                            Downloaded: fetched > 0,
                            $"{file.Name} does not verify ({problem}); nothing was put in place.");
                    }

                    File.Move(partial, target, true);
                    fetched++;
                }
                finally
                {
                    if (File.Exists(partial))
                    {
                        File.Delete(partial);
                    }
                }
            }
        }
        catch
        {
            // A fetch that failed must not leave litter behind: the directory is this call's, and an empty one
            // is exactly the trace it should not leave.
            RemoveDirectoryIfEmpty(model);
            throw;
        }

        return new EmbeddingModelInstallation(
            model,
            Present: true,
            Downloaded: true,
            $"Installed {fetched} file(s), {Megabytes(model.SizeBytes)} MB, through the '{source}' source; " +
            "checksums verified.");
    }

    /// <inheritdoc />
    public ValueTask<EmbeddingModelStatus> RemoveAsync(string modelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = Find(modelId);

        // The whole directory, so nothing of the model is left half-there and the space really comes back.
        var directory = paths.ModelDirectory(model.Id);
        if (!Directory.Exists(directory))
        {
            return ValueTask.FromResult(new EmbeddingModelStatus(
                model,
                Present: false,
                0,
                "Not installed, so there was nothing to remove."));
        }

        Directory.Delete(directory, recursive: true);
        return ValueTask.FromResult(new EmbeddingModelStatus(
            model,
            Present: false,
            0,
            $"Removed {model.Files.Count} file(s); the model is no longer installed."));
    }

    /// <summary>
    /// What is wrong with a file, or null when it is present, the right size and the recorded bytes.
    /// </summary>
    /// <remarks>
    /// Size before checksum: a truncated download is the common failure, and reading the whole file to hash it
    /// would only take longer to say the same thing.
    /// </remarks>
    private static async ValueTask<string?> ProblemAsync(
        string path,
        EmbeddingModelFile file,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return "not present";
        }

        if (info.Length != file.SizeBytes)
        {
            return $"wrong size: {info.Length} bytes, expected {file.SizeBytes}";
        }

        var hash = await FileHash.Sha256Async(path, cancellationToken);
        return string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"wrong checksum: {hash}";
    }

    private string PathOf(EmbeddingModel model, EmbeddingModelFile file) =>
        Path.Combine(paths.ModelDirectory(model.Id), file.Name);

    /// <summary>
    /// Takes back the model's directory when a failed call left it empty, so a refused download leaves nothing
    /// at all rather than an empty folder that reads like a half-installed model.
    /// </summary>
    private void RemoveDirectoryIfEmpty(EmbeddingModel model)
    {
        var directory = paths.ModelDirectory(model.Id);
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static string Megabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture);

    private EmbeddingModel Find(string modelId) =>
        models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"No embedding model is registered as '{modelId}'.");
}
