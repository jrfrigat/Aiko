using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// What a working directory turns out to be when Aiko is asked about it.
/// </summary>
public enum ProjectPathMatch
{
    /// <summary>
    /// Neither the folder nor any parent is a project: nothing registered, and no <c>.aiko</c> to find.
    /// </summary>
    None,

    /// <summary>
    /// The folder carries <c>.aiko</c> but the daemon does not know it - an initialization whose
    /// registration is missing.
    /// </summary>
    Initialized,

    /// <summary>A registered project owns this folder or one of its parents.</summary>
    Registered
}

/// <summary>
/// Resolves a working directory to the Aiko project it belongs to, and says which of the three states it is
/// in when there is no project.
/// </summary>
/// <remarks>
/// This is the question a user-scope skill cannot avoid. Such a skill is visible in every folder, while the
/// project it should act on is whichever folder the user has open - and in a client like Cline the MCP entry,
/// not the folder, is what carries a project. Resolution walks upwards, so opening a subfolder of a project
/// still finds that project rather than guessing.
/// </remarks>
public static class ProjectPathLookup
{
    /// <summary>
    /// Finds the project that owns <paramref name="path"/>, or reports why there is none.
    /// </summary>
    /// <param name="catalog">Catalog of the registered projects.</param>
    /// <param name="path">Working directory to resolve; it does not have to exist.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public static async ValueTask<ProjectPathLookupResult> ResolveAsync(
        IProjectCatalog catalog,
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var projects = await catalog.ListAsync(cancellationToken);

        foreach (var directory in Ancestors(fullPath))
        {
            var match = projects.FirstOrDefault(project => SameDirectory(project.RootPath, directory));
            if (match is not null)
            {
                return new ProjectPathLookupResult(ProjectPathMatch.Registered, match, directory);
            }
        }

        foreach (var directory in Ancestors(fullPath))
        {
            if (Directory.Exists(AikoProjectPaths.DataRoot(directory)))
            {
                return new ProjectPathLookupResult(ProjectPathMatch.Initialized, null, directory);
            }
        }

        return new ProjectPathLookupResult(ProjectPathMatch.None, null, fullPath);
    }

    /// <summary>
    /// The directory itself, then each parent up to the root.
    /// </summary>
    private static IEnumerable<string> Ancestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
        }
    }

    /// <summary>
    /// Whether two paths name the same directory, ignoring a trailing separator and case: Windows paths are
    /// not case-sensitive, and a catalog entry may have been written by an older Aiko.
    /// </summary>
    private static bool SameDirectory(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The answer to "which project is this folder?" - the project when there is one, the directory the answer
/// came from, and which of the three states applies when there is not.
/// </summary>
/// <param name="Match">What the directory is.</param>
/// <param name="Project">The owning project, or null when <paramref name="Match"/> is not Registered.</param>
/// <param name="Path">The directory that decided the answer.</param>
public sealed record ProjectPathLookupResult(
    ProjectPathMatch Match,
    RegisteredProject? Project,
    string Path);
