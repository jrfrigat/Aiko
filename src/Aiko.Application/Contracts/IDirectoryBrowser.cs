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
/// Directory browsing for the UI's project picker, done by the daemon: the File System Access API hands out
/// a handle whose name is only the last segment, and a project root has to be an absolute path.
/// </summary>
/// <remarks>Directories only - never file names or contents - and not exposed over MCP.</remarks>
public interface IDirectoryBrowser
{
    /// <summary>Lists the child directories of <paramref name="path"/>, or the filesystem roots when it is null or empty.</summary>
    /// <exception cref="ArgumentException">The path is not a usable directory: relative, nonexistent, or a file.</exception>
    ValueTask<DirectoryListing> ListAsync(string? path, CancellationToken cancellationToken);
}
