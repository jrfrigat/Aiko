namespace Aiko.Application.Contracts;

/// <summary>
/// One selectable directory in the project browser.
/// </summary>
/// <param name="Name">The directory's own name, as it is shown.</param>
/// <param name="Path">Its absolute path: what the daemon needs as a project root.</param>
/// <param name="IsAikoProject">Whether the directory already contains an <c>.aiko</c> tree.</param>
/// <param name="IsReadable">Whether the daemon may enter it. Unreadable directories are shown but cannot be picked.</param>
public sealed record DirectoryEntry(string Name, string Path, bool IsAikoProject, bool IsReadable);

/// <summary>
/// One level of the filesystem as the project browser sees it.
/// </summary>
/// <param name="Path">The listed directory, or null when this is the roots screen.</param>
/// <param name="Parent">The parent to go back to, or null at a root.</param>
/// <param name="IsReadable">Whether the listed directory itself could be read.</param>
/// <param name="Entries">Its immediate child directories, or the filesystem roots.</param>
public sealed record DirectoryListing(
    string? Path,
    string? Parent,
    bool IsReadable,
    IReadOnlyList<DirectoryEntry> Entries);

/// <summary>
/// Directory browsing for the UI's project picker.
/// </summary>
/// <remarks>
/// The browser cannot do this itself: the File System Access API hands out a handle whose
/// <c>name</c> is only the last segment, and the daemon needs an absolute path. The daemon already
/// runs on the user's machine, behind loopback and the access token, and knows the real path.
///
/// This is deliberately a directories-only listing: it never reads file names or contents, and it is
/// not exposed over MCP (an agent has its own project-scoped tools).
/// </remarks>
public interface IDirectoryBrowser
{
    /// <summary>
    /// Lists the child directories of <paramref name="path"/>, or the filesystem roots when it is null
    /// or empty.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the path is not a usable directory: relative, nonexistent, or a file.
    /// </exception>
    ValueTask<DirectoryListing> ListAsync(string? path, CancellationToken cancellationToken);
}
