using System.IO.Compression;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The archive a project's own files travel in: what <c>aiko backup</c> writes and <c>aiko restore</c> reads.
/// </summary>
/// <remarks>
/// The format is not this class's invention - it is the one the <c>aiko_backup</c> tool has always written:
/// a zip holding the <em>contents</em> of the project's <c>.aiko</c> directory, so that unpacking it into
/// that directory puts the project back. A second format would mean a backup only one of the two writers
/// could ever restore.
/// </remarks>
public static class ProjectArchive
{
    /// <summary>
    /// Zips the contents of a directory and then reads the archive back to say what it holds.
    /// </summary>
    /// <param name="sourceDirectory">Directory whose contents are archived, the project's <c>.aiko</c>.</param>
    /// <param name="targetPath">Archive to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the archive was found to contain.</returns>
    /// <exception cref="DirectoryNotFoundException">There is nothing to archive at that path.</exception>
    public static async Task<ArchiveSummary> CreateAsync(
        string sourceDirectory,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Nothing to archive: {sourceDirectory} does not exist.");
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // includeBaseDirectory false: the archive holds the contents, exactly as `aiko_backup` writes it.
        await Task.Run(
            () => ZipFile.CreateFromDirectory(
                sourceDirectory,
                targetPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false),
            cancellationToken);

        // Read back what was just written. "Done" is a claim; the count and the size are the evidence.
        using var archive = ZipFile.OpenRead(targetPath);
        return new ArchiveSummary(
            targetPath,
            archive.Entries.Count,
            archive.Entries.Sum(entry => entry.Length));
    }

    /// <summary>
    /// Unpacks an archive into a directory, refusing any entry that would land outside it.
    /// </summary>
    /// <param name="archivePath">Archive to unpack.</param>
    /// <param name="targetDirectory">Directory to unpack into; its contents are overwritten by name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many files were written.</returns>
    /// <exception cref="InvalidDataException">The archive holds an entry that would escape the target.</exception>
    public static async Task<int> ExtractAsync(
        string archivePath,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(targetDirectory);
        using var archive = ZipFile.OpenRead(archivePath);

        // Every destination is resolved and checked before anything is written: an archive that is hostile
        // half-way through must not leave half of itself on disk.
        var planned = new List<(ZipArchiveEntry Entry, string Destination)>();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            {
                // The archive's own root entry, which is the directory being restored into.
                continue;
            }

            // Zip-slip: an entry named `../something` would otherwise be written outside the project. The
            // separator is part of the comparison so a sibling directory whose name merely starts the same
            // does not pass.
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The archive holds an entry that would be written outside the project: {entry.FullName}");
            }

            planned.Add((entry, destination));
        }

        Directory.CreateDirectory(root);
        var written = 0;
        foreach (var (entry, destination) in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await Task.Run(() => entry.ExtractToFile(destination, overwrite: true), cancellationToken);
            written++;
        }

        return written;
    }
}

/// <summary>
/// What an archive was found to hold, read back from the file rather than assumed from the write.
/// </summary>
/// <param name="Path">Where the archive is.</param>
/// <param name="Entries">How many entries it holds.</param>
/// <param name="Bytes">The uncompressed size of those entries.</param>
public sealed record ArchiveSummary(string Path, int Entries, long Bytes);
