using Aiko.Application.Contracts;

namespace Aiko.Pwa.Services;

/// <summary>
/// The addresses the cockpit builds for its own pages.
/// </summary>
/// <remarks>
/// One place, because there were several: an address assembled at each call site is an address that can
/// drift, and one of them did - the related-cards graph built <c>/p/{id}/cards/{cardId}</c> from the
/// immutable project id that a card's relation stores, so a link a person copied out of the UI named the
/// project by a GUID instead of by its readable handle. The routes here always speak the handle, and
/// <see cref="ReadableHandle"/> says when a route value has to be replaced by one.
/// <para>
/// The rule stops at the UI: the REST routes and the MCP endpoint keep resolving both, because the id is
/// the key every card file, relation and execution is stored under.
/// </para>
/// </remarks>
internal static class ProjectRoutes
{
    /// <summary>The project's overview, which is also the prefix of every other page of that project.</summary>
    public static string Overview(string projectHandle) => $"/p/{Escape(projectHandle)}";

    /// <summary>The board, where a link to a project goes.</summary>
    public static string Board(string projectHandle) => Section(projectHandle, "board");

    /// <summary>A page of the project: the backlog, the settings, the links registry.</summary>
    public static string Section(string projectHandle, string section) =>
        $"{Overview(projectHandle)}/{Escape(section)}";

    /// <summary>
    /// The release screen: what the daemon knows about the project's releases, and where a release is asked
    /// for. Named because the rail links to it and the page answers at it, and a section name typed twice is
    /// a section that can drift.
    /// </summary>
    public static string Release(string projectHandle) => Section(projectHandle, "release");

    /// <summary>One card of the project.</summary>
    public static string Card(string projectHandle, string cardId) =>
        $"{Overview(projectHandle)}/cards/{Escape(cardId)}";

    /// <summary>The form that creates a card in the project.</summary>
    public static string NewCard(string projectHandle) => $"{Overview(projectHandle)}/cards/new";

    /// <summary>
    /// The readable handle to use when a route value names a project by its immutable id, or null when the
    /// value is already an address the UI keeps - a handle, an unknown project, or nothing at all.
    /// </summary>
    public static string? ReadableHandle(IEnumerable<RegisteredProject> projects, string routeValue)
    {
        if (string.IsNullOrWhiteSpace(routeValue))
        {
            return null;
        }

        var project = projects.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, routeValue, StringComparison.Ordinal));
        return project is { } found &&
            !string.Equals(found.Handle, routeValue, StringComparison.Ordinal)
            ? found.Handle
            : null;
    }

    /// <summary>
    /// The same address with the project segment replaced by its handle, or null when the value does not
    /// name a whole segment of that address. Everything after it - the page, the card, the query - is kept,
    /// so a deep link lands on the same screen it named.
    /// </summary>
    public static string? WithHandle(string absoluteUri, string routeValue, string projectHandle)
    {
        var marker = $"/p/{Escape(routeValue)}";
        var index = absoluteUri.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var after = index + marker.Length;
        if (after < absoluteUri.Length && absoluteUri[after] is not ('/' or '?'))
        {
            return null;
        }

        return $"{absoluteUri[..index]}/p/{Escape(projectHandle)}{absoluteUri[after..]}";
    }

    /// <summary>
    /// A handle, a slug and a card id are segments of a route: the characters that would end the segment
    /// early are escaped, and nothing else is touched.
    /// </summary>
    private static string Escape(string value) => Uri.EscapeDataString(value);
}
