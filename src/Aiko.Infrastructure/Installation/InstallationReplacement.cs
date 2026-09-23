using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>What a replacement did, and whether it had to repair an interrupted one first.</summary>
/// <param name="Version">What the installation now says it is.</param>
/// <param name="EntriesReplaced">How many entries of the installation were moved aside and replaced.</param>
/// <param name="RecoveredPrevious">
/// Whether an interrupted earlier replacement was repaired before this one ran. The caller reports it: a
/// repair that happens silently looks like an installation that was fine all along.
/// </param>
public sealed record ReplacementResult(InstalledVersion Version, int EntriesReplaced, bool RecoveredPrevious);

/// <summary>
/// Puts a staged release in place of what is installed, and can put the previous version back.
/// </summary>
/// <remarks>
/// The directory itself is never recreated: it is on the user's <c>PATH</c>, and an open shell holds the path
/// it resolved, so the entries inside it are what moves. Renaming inside one volume is cheap and reversible,
/// which is exactly what makes the rollback possible; replacing the directory would not be.
/// <para>
/// The caller must have stopped a running daemon first. That needs the port from the settings, a health
/// request and a restart, and it belongs to the verbs that drive an installation, not here.
/// </para>
/// </remarks>
public sealed class InstallationReplacement
{
    /// <summary>
    /// Replaces the installation with the staged release.
    /// </summary>
    /// <param name="staged">A verified, unpacked release, as <see cref="ReleaseStaging"/> prepares it.</param>
    /// <param name="installDirectory">Directory the release goes into.</param>
    /// <param name="updatePath">Whether the directory may be added to the user <c>PATH</c>.</param>
    public ReplacementResult Apply(StagedRelease staged, string installDirectory, bool updatePath = true)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        if (DescribeForeignContent(installDirectory) is { } foreign)
        {
            throw new InstallationRefusedException(foreign);
        }

        var recovered = RepairInterruptedReplacement(installDirectory);
        Directory.CreateDirectory(installDirectory);

        var previous = $"{installDirectory}.previous-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var replaced = Swap(installDirectory, staged.Directory, previous);

        var version = new InstalledVersion(
            staged.Tag,
            staged.Version,
            InstalledVersion.LatestChannel,
            DateTimeOffset.UtcNow,
            installDirectory);

        try
        {
            InstalledVersionFile.Write(installDirectory, version);
        }
        catch
        {
            // Recorded before the previous version is dropped, so a failure here can still put it back: while
            // the old entries are on disk, un-doing this is a rename, and afterwards it is nothing.
            RollBack(installDirectory, previous);
            throw;
        }

        Discard(previous);
        if (updatePath)
        {
            InstallationPath.AddToUserPath(installDirectory);
        }

        return new ReplacementResult(version, replaced, recovered);
    }

    /// <summary>
    /// Why the directory is not the installer's to fill, or null when it is.
    /// </summary>
    /// <remarks>
    /// A replacement moves every entry of the directory aside and drops them, which is right only for a
    /// directory that belongs to Aiko: one that does not exist or is empty, one holding an installation
    /// (<c>aiko.exe</c> or <c>install.json</c>), or one an interrupted replacement left a <c>previous</c> beside.
    /// Anything else - <c>-InstallDir D:\Tools</c>, or the data directory by mistake - holds someone else's
    /// files, and emptying it would delete them.
    /// </remarks>
    /// <param name="installDirectory">Directory the release would go into.</param>
    public static string? DescribeForeignContent(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        if (!Directory.Exists(installDirectory) ||
            !Directory.EnumerateFileSystemEntries(installDirectory).Any() ||
            File.Exists(Path.Combine(installDirectory, ReleaseLayout.CliFileName)) ||
            File.Exists(Path.Combine(installDirectory, InstallationFiles.VersionFileName)))
        {
            return null;
        }

        var full = Path.GetFullPath(installDirectory);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        if (parent is not null && Directory.Exists(parent) &&
            Directory.GetDirectories(parent, $"{name}.previous-*").Length > 0)
        {
            return null;
        }

        return $"{installDirectory} is not empty and holds no Aiko installation, so installing there would " +
            "delete what it holds. Choose an empty directory or one Aiko was installed into.";
    }

    /// <summary>
    /// Deals with a <c>previous</c> directory an earlier run left behind.
    /// </summary>
    /// <remarks>
    /// Two cases look alike on disk and must not be treated alike. An installation that is not whole - no
    /// entry point - and has a <c>previous</c> is one whose replacement died between the renames, and the
    /// previous version is what makes it work again. An installation that <b>is</b> whole and has a
    /// <c>previous</c> is one whose replacement finished and died during clean-up: putting that version back
    /// would undo a finished update, so it goes instead.
    /// </remarks>
    /// <returns>Whether the installation was recovered from a previous version.</returns>
    private static bool RepairInterruptedReplacement(string installDirectory)
    {
        var full = Path.GetFullPath(installDirectory);
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (parent is null || !Directory.Exists(parent))
        {
            return false;
        }

        var candidates = Directory.GetDirectories(parent, $"{name}.previous-*");
        if (candidates.Length == 0)
        {
            return false;
        }

        // By name descending, which for these stamps is also newest first: the newest is the one that was
        // about to become the installation.
        Array.Sort(candidates, (left, right) => string.CompareOrdinal(right, left));

        if (Exists(Path.Combine(installDirectory, ReleaseLayout.CliFileName)))
        {
            foreach (var candidate in candidates)
            {
                Discard(candidate);
            }

            return false;
        }

        DiscardContents(installDirectory);
        MoveEntries(candidates[0], installDirectory);
        foreach (var candidate in candidates)
        {
            Discard(candidate);
        }

        return true;
    }

    private static int Swap(string installDirectory, string stagedDirectory, string previousDirectory)
    {
        var movedAside = MoveEntries(installDirectory, previousDirectory);
        try
        {
            MoveEntries(stagedDirectory, installDirectory);
            return movedAside;
        }
        catch
        {
            RollBack(installDirectory, previousDirectory);
            throw;
        }
    }

    /// <summary>
    /// Puts the moved-aside entries back, leaving no trace of the attempt.
    /// </summary>
    /// <remarks>
    /// Everything the attempt put in is removed first, so the installation ends up exactly what it was rather
    /// than a mixture of both versions. An empty directory is the right result when nothing was installed
    /// before.
    /// </remarks>
    private static void RollBack(string installDirectory, string previousDirectory)
    {
        DiscardContents(installDirectory);
        MoveEntries(previousDirectory, installDirectory);
        Discard(previousDirectory);
    }

    private static int MoveEntries(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }

        Directory.CreateDirectory(to);
        var moved = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            MoveEntry(entry, Path.Combine(to, Path.GetFileName(entry)));
            moved++;
        }

        return moved;
    }

    private static void MoveEntry(string source, string destination)
    {
        // A destination in the way is what the rollback finds - the attempt's own entry - and it has to go
        // before the previous one can take its place.
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        else if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    /// <summary>Whether a path is there at all: an entry point is a file, the template tree a directory.</summary>
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void DiscardContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            Discard(entry);
        }
    }

    private static void Discard(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
