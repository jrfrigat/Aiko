namespace Aiko.Application.Contracts;

/// <summary>
/// The last release published in a project's repository, as the probe found it. <c>Known</c> is a value and
/// not an empty string on purpose: "the latest release is <c>v0.3.1</c>" and "I could not find out" are
/// different answers, and the second one comes with a <c>Failure</c> that says why - no repository, no
/// network, a 404 - instead of a screen that guesses.
/// </summary>
public sealed record LatestRelease(bool Known, string? Tag, string? Url, string? Failure)
{
    public static LatestRelease Unknown(string failure) => new(false, null, null, failure);

    public static LatestRelease Found(string tag, string url) => new(true, tag, url, null);
}

/// <summary>
/// The commit a project's working tree is on, read from its <c>.git</c> directory without running the git
/// client, so the answer does not depend on git being installed. What cannot be read is <c>Known</c> false
/// with a <c>Failure</c>; a detached HEAD is not a failure, it is a null branch.
/// </summary>
public sealed record RepositoryHead(bool Known, string? Branch, string? Commit, string? Failure)
{
    public static RepositoryHead Unknown(string failure) => new(false, null, null, failure);

    public static RepositoryHead Found(string? branch, string commit) => new(true, branch, commit, null);
}

/// <summary>
/// The facts a release screen shows: where releases are published, what the last one was, and what commit
/// the tree is on. <c>Repository</c> is <c>owner/repo</c>, or null when none is configured.
/// </summary>
public sealed record ReleaseInfo(
    ReleaseSettings Settings,
    string? Repository,
    LatestRelease Latest,
    RepositoryHead Head);

/// <summary>Gathers the facts a release screen shows, for a project named by id or by its readable handle.</summary>
public interface IReleaseInfoProvider
{
    /// <summary>Reads the release facts of one project.</summary>
    /// <exception cref="KeyNotFoundException">The project is not registered.</exception>
    ValueTask<ReleaseInfo> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads a working tree's HEAD out of its <c>.git</c> directory: separate from <see cref="IGitClient"/> on
/// purpose, because that one answers what only the git client can answer and reports "Git client unavailable"
/// when there is none, and a release screen has to keep working where git is not installed.
/// </summary>
public interface IGitRefReader
{
    /// <summary>Reads the HEAD of the repository a directory inside or below belongs to.</summary>
    /// <returns>The commit and branch, or <see cref="RepositoryHead.Unknown"/> with the reason.</returns>
    RepositoryHead Read(string directory);
}
