using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Release;

/// <summary>
/// Reads a repository's HEAD out of its <c>.git</c> directory, without running the git client.
/// </summary>
/// <remarks>
/// The daemon does not start external tools to answer a screen, and a machine where the release screen is
/// opened may have no git at all. Both point the same way: the two files git itself writes - <c>HEAD</c> and
/// <c>packed-refs</c> - are enough for the one fact here, which is the commit the tree is on.
/// <para>
/// Every failure is an answer rather than an exception: a directory that is not a repository and a ref that
/// cannot be read are things a screen shows, not failures of the daemon. This is the same reading as
/// <see cref="IGitClient"/>, which reports "Git client unavailable" instead of throwing.
/// </para>
/// </remarks>
public sealed class GitRefReader : IGitRefReader
{
    /// <summary>What a directory that holds no <c>.git</c> entry answers.</summary>
    internal const string NotARepository = "not a git repository";

    /// <summary>What an unreadable <c>HEAD</c> answers.</summary>
    internal const string UnreadableHead = "the repository's HEAD could not be read";

    /// <summary>
    /// How much of a sha is kept. Seven is what git abbreviates to by default, so the value a screen shows
    /// matches the one a person would read in their terminal.
    /// </summary>
    private const int ShortShaLength = 7;

    private const string RefPrefix = "ref: ";
    private const string BranchPrefix = "refs/heads/";
    private const string GitDirPrefix = "gitdir:";

    /// <inheritdoc />
    public RepositoryHead Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            if (ResolveGitDirectory(directory) is not { } gitDirectory)
            {
                return RepositoryHead.Unknown(NotARepository);
            }

            if (FirstLine(Path.Combine(gitDirectory, "HEAD")) is not { } head)
            {
                return RepositoryHead.Unknown(UnreadableHead);
            }

            // A HEAD that is not a symbolic ref holds the commit itself: a detached HEAD, which git prints as
            // a repository with a commit and no branch.
            if (!head.StartsWith(RefPrefix, StringComparison.Ordinal))
            {
                return RepositoryHead.Found(null, Shorten(head));
            }

            var reference = head[RefPrefix.Length..].Trim();
            if (ResolveReference(gitDirectory, reference) is not { } commit)
            {
                return RepositoryHead.Unknown($"the reference '{reference}' could not be read");
            }

            return RepositoryHead.Found(BranchOf(reference), Shorten(commit));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The .git directory belongs to another process that writes to it while we read, so a torn read is
            // an expected outcome and not a reason to fail the request.
            return RepositoryHead.Unknown(exception.Message);
        }
    }

    /// <summary>
    /// Finds the git directory of a working tree: <c>.git</c> as a directory, or as a file naming another
    /// directory - which is how a linked worktree and a submodule are laid out.
    /// </summary>
    private static string? ResolveGitDirectory(string directory)
    {
        var entry = Path.Combine(directory, ".git");
        if (Directory.Exists(entry))
        {
            return entry;
        }

        if (!File.Exists(entry) || FirstLine(entry) is not { } pointer ||
            !pointer.StartsWith(GitDirPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var target = pointer[GitDirPrefix.Length..].Trim();
        return target.Length == 0
            ? null
            : Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(directory, target));
    }

    /// <summary>
    /// Resolves a reference to its sha: the loose file git writes first, then the packed list a repack
    /// rewrites it into.
    /// </summary>
    private static string? ResolveReference(string gitDirectory, string reference)
    {
        // A ref is a path under .git and it comes from a file we do not own, so a reference that climbs out of
        // the git directory - by `..` or by being rooted outright - is refused rather than followed. Reading a
        // file of someone else's choosing and calling its first line a commit would be a lie the screen shows.
        if (reference.Length == 0 ||
            Path.IsPathRooted(reference) ||
            reference.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var loose = Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
        if (FirstLine(loose) is { } direct)
        {
            return direct;
        }

        var packed = Path.Combine(gitDirectory, "packed-refs");
        if (!File.Exists(packed))
        {
            return null;
        }

        foreach (var line in File.ReadLines(packed))
        {
            // `#` opens the header and `^` carries a peeled tag's commit; neither names a ref of its own.
            if (line.Length == 0 || line[0] is '#' or '^')
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[(separator + 1)..].Trim(), reference, StringComparison.Ordinal))
            {
                return line[..separator].Trim();
            }
        }

        return null;
    }

    /// <summary>The branch a reference names, or null for every other kind of ref.</summary>
    private static string? BranchOf(string reference) =>
        reference.StartsWith(BranchPrefix, StringComparison.Ordinal)
            ? reference[BranchPrefix.Length..]
            : null;

    private static string Shorten(string sha) =>
        sha.Length <= ShortShaLength ? sha : sha[..ShortShaLength];

    /// <summary>The first non-empty line of a file, trimmed, or null when the file cannot be read.</summary>
    private static string? FirstLine(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
