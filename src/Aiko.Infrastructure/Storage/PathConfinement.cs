namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The one check that keeps a path built from project data inside the directory it belongs to.
/// </summary>
/// <remarks>
/// Identifiers read from <c>.aiko</c> - a card's kind, a workflow's id, a type a command is named after - end up
/// in <see cref="Path.Combine(string, string)"/>, and a repository anyone can commit to is not a trusted source.
/// Two things take a path out of its root: a segment that climbs (<c>..</c>, an absolute path), which the full
/// path shows, and a junction or symbolic link somewhere below the root, which only the disk shows. Both are
/// refused. The root itself may be a link: that is the user's own layout, not something the data put there.
/// </remarks>
internal static class PathConfinement
{
    /// <summary>
    /// The full form of <paramref name="path"/>, after checking that it stays under <paramref name="root"/> and
    /// crosses no reparse point below it.
    /// </summary>
    /// <param name="root">Directory the path must stay inside.</param>
    /// <param name="path">Path to check, relative to the root or absolute.</param>
    /// <exception cref="ArgumentException">The path leaves the root or crosses a reparse point.</exception>
    public static string Resolve(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(fullRoot, path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(fullPath, fullRoot, comparison) &&
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException($"The path leaves {fullRoot}: {path}", nameof(path));
        }

        var current = fullRoot;
        foreach (var segment in Path.GetRelativePath(fullRoot, fullPath)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                // Nothing below a missing part exists yet, so nothing below it can be a link either.
                break;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ArgumentException($"The path crosses a link below {fullRoot}: {path}", nameof(path));
            }
        }

        return fullPath;
    }
}
