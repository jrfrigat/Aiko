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

    /// <summary>An invariant-culture integer, for the mono counters in the chrome.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A number with at most two decimals, invariant, for priorities and scores.</summary>
    public static string Score(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
