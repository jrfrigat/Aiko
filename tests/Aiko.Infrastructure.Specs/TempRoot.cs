namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Removes a fixture's temporary root - which is not the same thing as <see cref="Directory.Delete(string, bool)"/>.
/// </summary>
/// <remarks>
/// The roots these specs hand out hold a SQLite database that a CLI child process has just written, and
/// Windows keeps a hold on a file a process wrote for a moment after that process is gone: a recursive delete
/// issued in that moment throws <see cref="IOException"/>, and thrown out of a fixture's <c>Dispose</c> it
/// fails a test whose subject is somewhere else entirely (TASK-341).
/// <para>
/// The project already knows this: the daemon fixture of the MCP suite waits - "give the killed server a
/// moment to release its SQLite files" - before it deletes, and some fixtures here wrap the delete in a
/// <c>try</c>/<c>catch</c>. This is the same allowance, in one place, for the fixtures that run the CLI.
/// </para>
/// <para>
/// The path is guarded first: only a path under the temporary directory is removed, so an empty or relative
/// value fails loudly instead of deleting something else. A root that still refuses to go is reported on the
/// console and left behind - leftover temporary files are not this suite's subject, and a passing test must
/// not turn red over one.
/// </para>
/// </remarks>
internal static class TempRoot
{
    /// <summary>Attempts after the first one, with a growing pause between them: 200 ms up to 1 s.</summary>
    private const int Attempts = 5;

    public static void Delete(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
        {
            throw new InvalidOperationException(
                $"Refusing to remove a temporary root that is not a rooted path: '{root}'.");
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!normalized.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to remove a path outside the temporary directory: {normalized}");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                if (Directory.Exists(normalized))
                {
                    Directory.Delete(normalized, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    break;
                }

                // The system has not let go of a file this root holds yet. Waiting is what the MCP suite's own
                // fixture does for the same reason; it is cheaper than failing a test about someone else.
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }

        Console.WriteLine(
            $"[specs] left behind: {normalized} - the file system still holds it after {Attempts} attempts.");
    }
}
