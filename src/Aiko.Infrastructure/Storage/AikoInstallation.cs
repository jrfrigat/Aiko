namespace Aiko.Infrastructure.Storage;

/// <summary>
/// What an Aiko installation put on a machine, and how to take it back off.
/// </summary>
/// <remarks>
/// The installer publishes the daemon, the CLI and the stdio proxy into <c>&lt;data&gt;\bin</c> and adds that
/// directory to the user PATH; everything else Aiko owns lives in the data directory beside it. Undoing that
/// is three separate questions - the PATH entry, the binaries, and the data - and the data is never removed
/// without being asked for by name, because it holds every project's registration, the event journal and the
/// access token.
/// </remarks>
public sealed class AikoInstallation(AikoDataPaths paths)
{
    /// <summary>Directory the installer publishes into and adds to the user PATH.</summary>
    public string BinDirectory => Path.Combine(DataDirectory, "bin");

    /// <summary>Directory holding the database, the settings, the access token and the templates.</summary>
    public string DataDirectory =>
        Path.GetDirectoryName(paths.DatabasePath)
        ?? throw new InvalidOperationException("The database path must include a directory.");

    /// <summary>
    /// Removes one directory entry from a PATH value, keeping every other entry and their order.
    /// </summary>
    /// <param name="pathValue">PATH value to edit.</param>
    /// <param name="directory">Directory to drop.</param>
    /// <param name="removed">Whether the value named that directory at all.</param>
    /// <returns>The value without the entry.</returns>
    public static string RemovePathEntry(string pathValue, string directory, out bool removed)
    {
        var entries = pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var kept = entries.Where(entry => !Names(entry, directory)).ToArray();
        removed = kept.Length != entries.Length;
        return string.Join(';', kept);
    }

    /// <summary>
    /// Removes the PATH entry an install added, reporting whether there was one. A second run finds nothing
    /// and leaves the value alone rather than rewriting an identical string.
    /// </summary>
    public bool RemoveFromUserPath()
    {
        var current =
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        var updated = RemovePathEntry(current, BinDirectory, out var removed);
        if (removed)
        {
            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        }

        return removed;
    }

    /// <summary>
    /// Whether a PATH entry names the directory: compared case-insensitively and with trailing separators
    /// ignored, because both spellings are the same directory on Windows and either may be in the value.
    /// </summary>
    private static bool Names(string entry, string directory) =>
        string.Equals(
            entry.Trim().TrimEnd('\\', '/'),
            directory.Trim().TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes the published binaries, returning the files that could not be deleted.
    /// </summary>
    /// <remarks>
    /// The running <c>aiko</c> is one of these files on Windows, so an uninstall started from the installed
    /// CLI always leaves something behind. That is reported rather than treated as a failure: the caller is
    /// the process holding the file, and it can say so.
    /// </remarks>
    public InstallationRemoval RemoveBinaries()
    {
        if (!Directory.Exists(BinDirectory))
        {
            return InstallationRemoval.Nothing;
        }

        var blocked = new List<string>();
        foreach (var file in Directory.EnumerateFiles(BinDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                blocked.Add(file);
            }
        }

        // Directories the files left behind come out deepest-first; one still holding a locked file stays,
        // and it is reported through that file.
        foreach (var directory in Directory
                     .EnumerateDirectories(BinDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(candidate => candidate.Length))
        {
            TryDeleteDirectory(directory);
        }

        TryDeleteDirectory(BinDirectory);
        return new InstallationRemoval(true, blocked);
    }

    /// <summary>
    /// Deletes the daemon's own data directory - database, settings, token, backed-up archives and templates.
    /// </summary>
    /// <returns>What was there and what could not be deleted.</returns>
    public InstallationRemoval RemoveData() => RemoveContents(DataDirectory);

    /// <summary>
    /// Deletes one project's <c>.aiko</c> directory. Callers list what they are about to remove and ask
    /// first: Aiko never removes project files on its own.
    /// </summary>
    /// <param name="projectRootPath">Root of the project whose <c>.aiko</c> directory goes.</param>
    /// <returns>What was there and what could not be deleted.</returns>
    public InstallationRemoval RemoveProjectData(string projectRootPath) =>
        RemoveContents(AikoProjectPaths.DataRoot(projectRootPath));

    private static InstallationRemoval RemoveContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return InstallationRemoval.Nothing;
        }

        var blocked = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            blocked.AddRange(RemoveEntry(entry));
        }

        if (blocked.Count == 0)
        {
            TryDeleteDirectory(directory);
        }

        return new InstallationRemoval(true, blocked);
    }

    private static IReadOnlyList<string> RemoveEntry(string entry)
    {
        if (Directory.Exists(entry))
        {
            var blocked = new List<string>();
            foreach (var child in Directory.EnumerateFileSystemEntries(entry))
            {
                blocked.AddRange(RemoveEntry(child));
            }

            if (blocked.Count == 0)
            {
                TryDeleteDirectory(entry);
            }

            return blocked;
        }

        try
        {
            File.Delete(entry);
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [entry];
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory that still holds something is reported through that something.
        }
    }
}

/// <summary>
/// What one removal found and what it could not take.
/// </summary>
/// <param name="Existed">Whether the thing was on disk at all.</param>
/// <param name="Blocked">Entries that could not be deleted, by path.</param>
/// <remarks>
/// The two together are what an uninstall has to say: a second run removes nothing because nothing is left,
/// and that is different from a first run that could not remove everything because the running program holds
/// its own files.
/// </remarks>
public sealed record InstallationRemoval(bool Existed, IReadOnlyList<string> Blocked)
{
    /// <summary>Nothing was there to remove.</summary>
    public static InstallationRemoval Nothing { get; } = new(false, []);

    /// <summary>Whether something was removed.</summary>
    public bool Removed => Existed;
}
