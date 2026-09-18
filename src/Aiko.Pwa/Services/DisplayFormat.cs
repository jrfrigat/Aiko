using System.Globalization;
using Aiko.Application.Agents;

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

    /// <summary>How the dashboard reports one agent. Installed and connected are separate facts.</summary>
    public enum AgentPresence
    {
        /// <summary>The agent's executable was not found on PATH.</summary>
        NotDetected,

        /// <summary>Installed, but Aiko has not written its global skills and commands.</summary>
        NotConnected,

        /// <summary>Connected, but only some of Aiko's files are there - an outdated or partial install.</summary>
        PartiallyConnected,

        /// <summary>Installed, with the whole global configuration in place.</summary>
        Connected
    }

    /// <summary>
    /// Classifies an adapter for the dashboard. Detection comes from PATH, connection from Aiko's own
    /// files, and the two are independent: an agent can be installed without ever having been connected,
    /// which is exactly the state a single "not found" label used to hide.
    /// </summary>
    public static AgentPresence Classify(AgentAdapterOption adapter)
    {
        if (adapter.Installations.Count == 0)
        {
            return AgentPresence.NotDetected;
        }

        if (adapter.UserScope is { } scope)
        {
            if (scope.IsConfigured())
            {
                return AgentPresence.Connected;
            }

            if (scope.IsPartial())
            {
                return AgentPresence.PartiallyConnected;
            }
        }

        return AgentPresence.NotConnected;
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

    /// <summary>
    /// A caption with how much sits behind it: <c>Title (N)</c>. The count is always printed, zero included -
    /// a zero is a fact about the card, and a caption that drops it reads as "not loaded yet" rather than
    /// "nothing here". Parentheses and digits read the same in every language, so the shape needs no key.
    /// </summary>
    public static string Counted(string title, int count) =>
        $"{title} ({count.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>A number with at most two decimals, invariant, for priorities and scores.</summary>
    public static string Score(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A card's priority the way the board and the card page state it: the formula's score is a share of the
    /// maximum - the weighted criterion values, times the size coefficient - so it is printed on a 0..100
    /// scale. A reader compares cards by that figure, and "0,47" next to "1" says nothing to anyone.
    /// </summary>
    /// <remarks>
    /// The figure can pass 100, because the size coefficient rewards a small step: a card with the best
    /// possible criterion scores and an XS size is worth more than one of the same value at XL. Rounding is
    /// away from zero, so two cards a thousandth apart still read as different figures.
    /// </remarks>
    public static string Priority(decimal value) =>
        Math.Round(value * 100m, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
}
