using System.Globalization;

namespace Aiko.Pwa.Services;

/// <summary>
/// The small formatting helpers the shell and the pages share, so a project's avatar and path tag look
/// the same wherever they are drawn.
/// </summary>
public static class DisplayFormat
{
    /// <summary>Up to two initials of a project name, for its avatar.</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }

    /// <summary>
    /// The last two segments of a project root, so the rail's tag says <c>FrigaT/StitchFlow</c> rather
    /// than the whole path. The path is split on both separators: the client runs in a browser, where
    /// <see cref="Path"/> would not treat a Windows backslash as one.
    /// </summary>
    public static string ProjectTag(string path)
    {
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => path,
            1 => parts[0],
            _ => $"{parts[^2]}/{parts[^1]}"
        };
    }

    /// <summary>
    /// What the agent card shows for one adapter: the reported version when there is one, otherwise the
    /// executable that was found, and <paramref name="notFound"/> when nothing was found at all.
    /// </summary>
    /// <remarks>
    /// The fallback is the point. Adapters discover an installation by scanning PATH for the executable
    /// and do not run it, so <see cref="Aiko.Application.Agents.AgentInstallation.Version"/> is null on
    /// every adapter today - and `installation?.Version ?? notFound` then printed "not found" for agents
    /// that were present. "Found, version unknown" and "not found" are different states.
    /// </remarks>
    public static string AgentState(Aiko.Application.Agents.AgentInstallation? installation, string notFound)
    {
        if (installation is null)
        {
            return notFound;
        }

        if (!string.IsNullOrWhiteSpace(installation.Version))
        {
            return installation.Version;
        }

        // The executable's own name is the honest answer to "where did you find it?", and it is what a
        // person needs to see when the version is unknown.
        var name = LastSegment(installation.ExecutablePath);
        return name.Length > 0 ? name : notFound;
    }

    // The last path segment, split on both separators: the client runs in a browser, where Path would
    // not treat a Windows backslash as one.
    private static string LastSegment(string path)
    {
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    /// <summary>An invariant-culture integer, for the mono counters in the chrome.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A number with at most two decimals, invariant, for priorities and scores.</summary>
    public static string Score(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
