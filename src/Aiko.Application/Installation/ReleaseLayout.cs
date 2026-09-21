namespace Aiko.Application.Installation;

/// <summary>
/// The entry points a release promises to carry.
/// </summary>
/// <remarks>
/// This is the release's own list, not a convenient subset: <c>.github/workflows/release.yml</c> refuses to
/// publish a release that lacks any of them, so an engine that accepted less would install a release the
/// release itself considers broken - one without the stdio proxy, which every MCP client needs, or without
/// the project template. The two lists have to move together: a release may publish more than this, but not
/// less.
/// </remarks>
public static class ReleaseLayout
{
    /// <summary>Entries that must exist in an unpacked release, relative to its root.</summary>
    public static IReadOnlyList<string> RequiredEntries { get; } =
    [
        "aiko.exe",
        "aiko-stdio.exe",
        Path.Combine("server", "Aiko.Server.exe"),
        Path.Combine("server", "wwwroot", "index.html"),
        Path.Combine("templates", "default", "template.json")
    ];
}
