namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Paths inside the project's .aiko data directory.
/// </summary>
public static class AikoProjectPaths
{
    public const string DirectoryName = ".aiko";

    /// <summary>Name of the directory that holds the workflow definitions and, under them, the card collections.</summary>
    /// <remarks>
    /// One directory holds both, and the two kinds of entry are told apart by their shape rather than by a
    /// list: a <c>.json</c> file is a workflow definition (<c>workflows/task.json</c>), a directory is the
    /// collection of the cards of that type (<c>workflows/tasks/TASK-1/</c>). That is what lets a project
    /// name a type anything at all, since nothing it chooses can collide with a service directory.
    /// </remarks>
    public const string WorkflowsDirectoryName = "workflows";

    /// <summary>Name of the directory holding the rebuilt SQLite projections of the project's data.</summary>
    /// <remarks>
    /// Service state rather than project data: it is derived from the files and rebuilt by <c>aiko reindex</c>,
    /// so nothing about it is worth recording or comparing.
    /// </remarks>
    public const string ProjectionsDirectoryName = "projections";

    /// <summary>Name of the directory holding the state of runs in flight.</summary>
    /// <remarks>
    /// Service state, and the one directory that changes while nothing of the user's does: treating it as
    /// project data would report Aiko's own work as a change made outside Aiko.
    /// </remarks>
    public const string RuntimeDirectoryName = "runtime";

    public static string DataRoot(string projectRoot) =>
        Path.Combine(projectRoot, DirectoryName);

    public static string CardCollectionsRoot(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), WorkflowsDirectoryName);

    /// <summary>Returns the path of the document holding what an agent must do right after the project was created.</summary>
    /// <remarks>
    /// A file rather than a field somewhere: it is written once, at init, from the template the project was
    /// created from, and from then on belongs to the project like any other document it owns. Markdown keeps
    /// it readable and editable without Aiko.
    /// </remarks>
    /// <param name="projectRoot">Root directory of the project.</param>
    public static string InitializationDocument(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), "initialization.md");

    /// <summary>Returns the path of the document holding the checksums of the project's data.</summary>
    /// <remarks>
    /// The baseline <c>aiko verify</c> compares against, written only when someone seals one. It sits beside
    /// the data it covers rather than in the daemon's database, so a check that finds no manifest says so
    /// rather than inventing one.
    /// </remarks>
    /// <param name="projectRoot">Root directory of the project.</param>
    public static string IntegrityManifest(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), "manifest.json");
}
