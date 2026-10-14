namespace Aiko.Application.Contracts;

/// <summary>
/// What the git client says about one project directory. <c>Failure</c> carries why git could not answer -
/// "Git client unavailable" when there is no executable, the client's own words when it refused - and is null
/// when everything was read.
/// </summary>
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

/// <summary>One commit, as the log prints it.</summary>
public sealed record GitCommit(string Sha, string ShortSha, string Author, DateTimeOffset CommittedAt, string Message);

/// <summary>
/// One file of a diff: what changed and the patch itself. <c>Path</c> is relative to the repository root, and
/// <c>Patch</c> is empty when the file is binary.
/// </summary>
public sealed record GitFileDiff(string Path, int Added, int Removed, string Patch);

/// <summary>
/// The diff of a card's files: the patches of the files it declared or actually touched. <c>Failure</c> says
/// why none could be computed - "Git client unavailable", "not a git repository", the client's own words -
/// and is null when the diff was computed, even if it turned out to be empty.
/// </summary>
public sealed record GitCardDiff(bool Available, string? Failure, IReadOnlyList<GitFileDiff> Files)
{
    public static GitCardDiff Unavailable(string reason) => new(false, reason, []);
}

/// <summary>
/// The git side of a project, as one screen reads it: the status of the directory and its recent commits,
/// newest first - empty when git could not answer.
/// </summary>
public sealed record GitOverview(GitStatus Status, IReadOnlyList<GitCommit> Commits);

/// <summary>Reads a project's git state by running the <c>git</c> client as a command.</summary>
/// <remarks>
/// Aiko does not reimplement git and does not link a library: the client on the machine is the authority.
/// That is also why every method answers rather than throws - "Git client unavailable" and "not a git
/// repository" are states a screen has to show, not errors that should break a page.
/// </remarks>
public interface IGitClient
{
    /// <summary>Reads what git says about a directory: version, repository, branch, remote, changes, HEAD.</summary>
    ValueTask<GitStatus> StatusAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Reads the last commits of a repository, newest first.</summary>
    ValueTask<IReadOnlyList<GitCommit>> LogAsync(
        string directory,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the diff of the files a card touched: what is uncommitted against HEAD, and the last commit that
    /// changed each file when nothing is pending. This is what the card's code-changes block draws.
    /// </summary>
    ValueTask<GitCardDiff> DiffAsync(
        string directory,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}
