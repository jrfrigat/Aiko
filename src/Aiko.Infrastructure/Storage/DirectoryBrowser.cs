using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Filesystem directory browsing for the UI's project picker.
/// </summary>
/// <remarks>
/// Directories only, and no filesystem walking: every call lists one level of one directory the user
/// asked for, so the picker cannot be used to inventory a machine. Symbolic links and junctions are
/// skipped - following one would both escape the directory the breadcrumb claims and make the path
/// shown to the user a lie.
/// </remarks>
public sealed class DirectoryBrowser : IDirectoryBrowser
{
    /// <inheritdoc />
    public ValueTask<DirectoryListing> ListAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return ValueTask.FromResult(new DirectoryListing(null, null, true, Roots()));
        }

        if (!Path.IsPathFullyQualified(path))
        {
            // A relative path would resolve against the daemon's working directory, which is not
            // anything the user chose; refuse rather than guess.
            throw new ArgumentException("The path must be absolute.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new ArgumentException("The path is a file, not a directory.", nameof(path));
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException("The directory does not exist.", nameof(path));
        }

        return ValueTask.FromResult(ListChildren(fullPath));
    }

    private static DirectoryListing ListChildren(string fullPath)
    {
        var entries = new List<DirectoryEntry>();
        var readable = true;
        try
        {
            foreach (var directory in new DirectoryInfo(fullPath).EnumerateDirectories())
            {
                // Reparse points are skipped outright: a link's target is not where the user thinks
                // they are, and resolving it would silently widen the browse.
                if (directory.LinkTarget is not null)
                {
                    continue;
                }

                entries.Add(new DirectoryEntry(
                    directory.Name,
                    directory.FullName,
                    HasAikoTree(directory.FullName),
                    CanEnter(directory)));
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // An unreadable directory is a state the picker shows, not an error the request fails on.
            readable = false;
        }

        entries.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath));
        return new DirectoryListing(fullPath, parent, readable, entries);
    }

    private static bool HasAikoTree(string path) =>
        Directory.Exists(AikoProjectPaths.DataRoot(path));

    private static bool CanEnter(DirectoryInfo directory)
    {
        try
        {
            // Reading one entry is the cheapest honest answer to "may the daemon go in here?".
            return directory.EnumerateFileSystemInfos().Any();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static IReadOnlyList<DirectoryEntry> Roots()
    {
        var roots = new List<DirectoryEntry>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                bool ready;
                try
                {
                    ready = drive.IsReady;
                }
                catch (IOException)
                {
                    ready = false;
                }

                if (!ready)
                {
                    continue;
                }

                roots.Add(new DirectoryEntry(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    false,
                    true));
            }

            return roots;
        }

        roots.Add(new DirectoryEntry("/", "/", false, true));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && Directory.Exists(home))
        {
            roots.Add(new DirectoryEntry(home, home, HasAikoTree(home), true));
        }

        return roots;
    }
}
