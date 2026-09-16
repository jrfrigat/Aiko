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
}
