namespace Aiko.Application.Contracts;

/// <summary>
/// What the git client says about one project directory.
/// </summary>
/// <param name="Available">Whether a <c>git</c> executable answered at all.</param>
/// <param name="Version">The client's own version line, when it answered.</param>
/// <param name="Failure">
/// Why git could not answer - "Git client unavailable" when there is no executable, the client's own words
/// when it refused. Null when everything was read.
/// </param>
/// <param name="IsRepository">Whether the directory is inside a git working tree.</param>
/// <param name="RepositoryRoot">Top level of that working tree.</param>
/// <param name="Branch">Current branch, or null on a detached HEAD.</param>
/// <param name="RemoteUrl">URL of <c>origin</c>, when the repository has one.</param>
/// <param name="ChangedFiles">Files with staged or unstaged changes, from <c>git status</c>.</param>
/// <param name="HeadSha">Short sha of HEAD, when the repository has a commit.</param>
/// <param name="HeadMessage">Subject of that commit.</param>
public sealed record GitStatus(
    bool Available,
    string? Version,
    string? Failure,
    bool IsRepository,
    string? RepositoryRoot,
    string? Branch,
    string? RemoteUrl,
    int ChangedFiles,
    string? HeadSha,
    string? HeadMessage);

/// <summary>
/// One commit, as the log prints it.
/// </summary>
/// <param name="Sha">Full sha.</param>
/// <param name="ShortSha">Abbreviated sha, as git abbreviates it.</param>
/// <param name="Author">Author name.</param>
/// <param name="CommittedAt">Commit time, with its offset.</param>
/// <param name="Message">Subject line.</param>
public sealed record GitCommit(string Sha, string ShortSha, string Author, DateTimeOffset CommittedAt, string Message);

/// <summary>
/// One file of a diff: what changed and the patch itself.
/// </summary>
/// <param name="Path">Path relative to the repository root.</param>
/// <param name="Added">Lines added, from <c>--numstat</c>.</param>
/// <param name="Removed">Lines removed.</param>
/// <param name="Patch">The unified patch, or an empty string when the file is binary.</param>
public sealed record GitFileDiff(string Path, int Added, int Removed, string Patch);

/// <summary>
/// The diff of a card's files: the patches of the files it declared or actually touched.
/// </summary>
/// <param name="Available">Whether the diff could be computed at all.</param>
/// <param name="Failure">
/// Why not - "Git client unavailable", "not a git repository", or the client's own words. Null when the diff
/// was computed, even if it turned out to be empty.
/// </param>
/// <param name="Files">One entry per changed file, in path order.</param>
public sealed record GitCardDiff(bool Available, string? Failure, IReadOnlyList<GitFileDiff> Files)
{
    /// <summary>A diff that could not be computed, with the reason.</summary>
    public static GitCardDiff Unavailable(string reason) => new(false, reason, []);
}

/// <summary>
/// The git side of a project, as one screen reads it: the status of the directory and its recent commits.
/// </summary>
/// <param name="Status">What git says about the project directory.</param>
/// <param name="Commits">The most recent commits, newest first; empty when git could not answer.</param>
public sealed record GitOverview(GitStatus Status, IReadOnlyList<GitCommit> Commits);

/// <summary>
/// Reads a project's git state by running the <c>git</c> client as a command.
/// </summary>
/// <remarks>
/// Aiko does not reimplement git and does not link a library: the client on the machine is the authority,
/// and a repository is the user's, not ours. That is also why every method here answers rather than throws -
/// "Git client unavailable" and "not a git repository" are states a screen has to show, not errors that
/// should break a page.
/// </remarks>
public interface IGitClient
{
    /// <summary>
    /// Reads what git says about a directory: client version, repository, branch, remote, changes and HEAD.
    /// </summary>
    /// <param name="directory">Any directory inside or below the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<GitStatus> StatusAsync(string directory, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the last commits of a repository, newest first.
    /// </summary>
    /// <param name="directory">Any directory inside or below the repository.</param>
    /// <param name="count">How many commits to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<GitCommit>> LogAsync(
        string directory,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the diff of the files a card touched: what is uncommitted against HEAD, and the last commit
    /// that changed each file when nothing is pending. This is what the card's code-changes block draws.
    /// </summary>
    /// <param name="directory">Any directory inside or below the repository.</param>
    /// <param name="paths">Files of the card, relative to the repository root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<GitCardDiff> DiffAsync(
        string directory,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}
