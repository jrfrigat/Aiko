namespace Aiko.Application.Contracts;

/// <summary>
/// The last release published in a project's repository, as the probe found it.
/// </summary>
/// <remarks>
/// <see cref="Known"/> is a value and not an empty string, which is the whole point of the shape: "the
/// latest release is <c>v0.3.1</c>" and "I could not find out" are different answers, and a screen that
/// only had a nullable tag would draw the second one as a blank where a version belongs. When
/// <see cref="Known"/> is false, <see cref="Failure"/> says why - no repository configured, no network, a
/// 404 - and a screen shows that reason instead of guessing.
/// </remarks>
/// <param name="Known">Whether the last release was actually resolved.</param>
/// <param name="Tag">Release tag, when it was.</param>
/// <param name="Url">Address of the release, when it was.</param>
/// <param name="Failure">Why not, when it was not.</param>
public sealed record LatestRelease(bool Known, string? Tag, string? Url, string? Failure)
{
    /// <summary>A probe that could not resolve the release, with the reason.</summary>
    public static LatestRelease Unknown(string failure) => new(false, null, null, failure);

    /// <summary>A probe that resolved the release.</summary>
    public static LatestRelease Found(string tag, string url) => new(true, tag, url, null);
}

/// <summary>
/// The commit a project's working tree is on, read from its <c>.git</c> directory.
/// </summary>
/// <remarks>
/// Read without running the <c>git</c> client, so the answer does not depend on git being installed on the
/// machine, and so a screen does not wait on a process. What cannot be read is <see cref="Known"/> false
/// with a <see cref="Failure"/> - a branch that is merely absent (a detached HEAD) is not a failure.
/// </remarks>
/// <param name="Known">Whether the repository and its HEAD were read.</param>
/// <param name="Branch">Current branch, or null on a detached HEAD.</param>
/// <param name="Commit">Abbreviated sha of HEAD, as the client would print it.</param>
/// <param name="Failure">Why not, when the state is not known.</param>
public sealed record RepositoryHead(bool Known, string? Branch, string? Commit, string? Failure)
{
    /// <summary>A state that could not be read, with the reason.</summary>
    public static RepositoryHead Unknown(string failure) => new(false, null, null, failure);

    /// <summary>A state that was read; the branch is null when HEAD is detached.</summary>
    public static RepositoryHead Found(string? branch, string commit) => new(true, branch, commit, null);
}

/// <summary>
/// The facts a release screen shows: where releases are published, what the last one was, and what commit
/// the tree is on.
/// </summary>
/// <param name="Settings">The effective release section of the project's settings.</param>
/// <param name="Repository">The repository as <c>owner/repo</c>, or null when none is configured.</param>
/// <param name="Latest">What the probe of the last release found.</param>
/// <param name="Head">What the project's <c>.git</c> directory says about its HEAD.</param>
public sealed record ReleaseInfo(
    ReleaseSettings Settings,
    string? Repository,
    LatestRelease Latest,
    RepositoryHead Head);

/// <summary>
/// Gathers the facts a release screen shows.
/// </summary>
public interface IReleaseInfoProvider
{
    /// <summary>
    /// Reads the release facts of one project.
    /// </summary>
    /// <param name="projectId">Project to read, by id or by its readable handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">The project is not registered.</exception>
    ValueTask<ReleaseInfo> ReadAsync(string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads a working tree's HEAD out of its <c>.git</c> directory, without running the git client.
/// </summary>
/// <remarks>
/// Separate from <see cref="IGitClient"/> on purpose. That one answers questions the git client alone can
/// answer and reports "Git client unavailable" when there is none; this one answers the one question whose
/// answer is a file, so a release screen keeps working on a machine where git is not installed.
/// </remarks>
public interface IGitRefReader
{
    /// <summary>
    /// Reads the HEAD of the repository a directory belongs to.
    /// </summary>
    /// <param name="directory">Any directory inside or below the repository.</param>
    /// <returns>The commit and branch, or <see cref="RepositoryHead.Unknown"/> with the reason.</returns>
    RepositoryHead Read(string directory);
}
