namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Finds agent executables in the directories of the PATH environment variable,
/// honoring Windows extensions (.exe/.cmd/.bat).
/// </summary>
internal static class ExecutableDetector
{
    /// <summary>
    /// Returns the sorted list of full paths of the found executables.
    /// </summary>
    public static IReadOnlyList<string> Find(IReadOnlyList<string> executableNames)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in pathEntries)
        {
            foreach (var executableName in executableNames)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, executableName + extension);
                    if (File.Exists(candidate))
                    {
                        found.Add(Path.GetFullPath(candidate));
                    }
                }
            }
        }

        return found.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
