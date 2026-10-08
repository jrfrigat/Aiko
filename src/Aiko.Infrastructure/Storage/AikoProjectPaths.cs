namespace Aiko.Infrastructure.Storage;

/// <summary>
/// Paths inside the project's .aiko data directory.
/// </summary>
public static class AikoProjectPaths
{
    /// <summary>
    /// Name of the root Aiko data directory in a project.
    /// </summary>
    public const string DirectoryName = ".aiko";

    /// <summary>
    /// Name of the directory that holds the workflow definitions and, under them, the card collections.
    /// </summary>
    /// <remarks>
    /// One directory holds both, and the two kinds of entry are told apart by their shape rather than by a
    /// list: a <c>.json</c> file is a workflow definition (<c>workflows/task.json</c>), a directory is the
    /// collection of the cards of that type (<c>workflows/tasks/TASK-1/</c>). That is what lets a project
    /// name a type anything at all - nothing it chooses can collide with a service directory, because the
    /// service directories are not where cards live.
    /// </remarks>
    public const string WorkflowsDirectoryName = "workflows";

    /// <summary>
    /// Name of the directory holding the rebuilt SQLite projections of the project's data.
    /// </summary>
    /// <remarks>
    /// Service state rather than project data: it is derived from the files and rebuilt by <c>aiko reindex</c>,
    /// so nothing about it is worth recording or comparing.
    /// </remarks>
    public const string ProjectionsDirectoryName = "projections";

    /// <summary>
    /// Name of the directory holding the state of runs in flight.
    /// </summary>
    /// <remarks>
    /// Service state rather than project data, and the one directory that changes while nothing of the user's
    /// does: a running stage rewrites its journal, so treating it as project data would report Aiko's own work
    /// as a change made outside Aiko.
    /// </remarks>
    public const string RuntimeDirectoryName = "runtime";

    /// <summary>
    /// Returns the full path of the .aiko directory in the project root.
    /// </summary>
    public static string DataRoot(string projectRoot) =>
        Path.Combine(projectRoot, DirectoryName);

    /// <summary>
    /// Returns the directory holding the project's card collections: <c>.aiko/workflows</c>.
    /// </summary>
    public static string CardCollectionsRoot(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), WorkflowsDirectoryName);

    /// <summary>
    /// Returns the path of the document holding what an agent must do right after the project was created.
    /// </summary>
    /// <remarks>
    /// A file rather than a field somewhere: the instruction is written once, at init, from the template the
    /// project was created from, and from then on it belongs to the project like any other document it owns.
    /// Keeping it as Markdown makes it readable and editable without Aiko, which is what a person who wants
    /// to see why a project was scaffolded a certain way will reach for.
    /// </remarks>
    /// <param name="projectRoot">Root directory of the project.</param>
    public static string InitializationDocument(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), "initialization.md");

    /// <summary>
    /// Returns the path of the document holding the checksums of the project's data.
    /// </summary>
    /// <remarks>
    /// The baseline <c>aiko verify</c> compares against, written only when someone seals one. It sits beside the
    /// data it covers rather than in the daemon's database, so deleting it, reading it or copying it to another
    /// machine takes no tool at all - and a check that finds no manifest says so rather than inventing one.
    /// </remarks>
    /// <param name="projectRoot">Root directory of the project.</param>
    public static string IntegrityManifest(string projectRoot) =>
        Path.Combine(DataRoot(projectRoot), "manifest.json");
}
