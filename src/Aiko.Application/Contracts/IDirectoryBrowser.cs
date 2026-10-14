namespace Aiko.Application.Contracts;

/// <summary>One selectable directory in the project browser; <c>Path</c> is absolute.</summary>
/// <remarks>An unreadable directory is listed but cannot be picked.</remarks>
public sealed record DirectoryEntry(string Name, string Path, bool IsAikoProject, bool IsReadable);

/// <summary>One level of the filesystem as the project browser sees it.</summary>
/// <remarks>A null <c>Path</c> is the roots screen; a null <c>Parent</c> is a root.</remarks>
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
