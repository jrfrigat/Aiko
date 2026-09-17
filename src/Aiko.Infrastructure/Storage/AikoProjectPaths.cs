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
    /// Returns the full path of the .aiko directory in the project root.
    /// </summary>
    public static string DataRoot(string projectRoot) =>
        Path.Combine(projectRoot, DirectoryName);

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
}
