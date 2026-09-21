using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// The user <c>PATH</c> entry an installation adds, and the rule for whether it is already there.
/// </summary>
/// <remarks>
/// A directory the user <c>PATH</c> already names is left exactly as it is: an installer that rewrote the
/// value every run would reorder a value a person may have edited, and rewriting it identically is still a
/// change to a machine-wide setting. Adding is therefore conditional, and the condition is decided by the
/// same rule the removal uses.
/// </remarks>
public static class InstallationPath
{
    /// <summary>
    /// Adds a directory to a PATH value, reporting whether the value changed.
    /// </summary>
    /// <remarks>
    /// Whether the directory is already named is answered by <see cref="AikoInstallation.RemovePathEntry"/>:
    /// it is the one place that knows a trailing separator or another case is still the same directory, and
    /// asking it is what keeps the project from having two answers to one question. Its returned value is
    /// dropped - only its answer to "was it there?" is wanted.
    /// </remarks>
    /// <param name="pathValue">Current PATH value, or null when there is none.</param>
    /// <param name="directory">Directory to add.</param>
    /// <param name="added">Whether the directory was added, so the caller can skip an identical write.</param>
    /// <returns>The value to store.</returns>
    public static string AddPathEntry(string? pathValue, string directory, out bool added)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var current = pathValue ?? string.Empty;

        _ = AikoInstallation.RemovePathEntry(current, directory, out var alreadyThere);
        if (alreadyThere)
        {
            added = false;
            return current;
        }

        added = true;
        var trimmed = current.TrimEnd(';');
        return trimmed.Length == 0 ? directory : $"{trimmed};{directory}";
    }

    /// <summary>
    /// Adds the directory to the user <c>PATH</c>, returning whether anything changed.
    /// </summary>
    /// <remarks>
    /// The only part of this type that touches the machine, and deliberately thin: the decision is in
    /// <see cref="AddPathEntry"/>, which is what the specs exercise, because a spec that wrote the user
    /// <c>PATH</c> would edit the owner's environment to prove a point.
    /// </remarks>
    /// <param name="directory">Directory to add.</param>
    public static bool AddToUserPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var updated = AddPathEntry(current, directory, out var added);
        if (added)
        {
            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        }

        return added;
    }
}
