namespace Aiko.Application.Contracts;

/// <summary>
/// The MCP endpoint of one project: the single address an agent's configuration may carry for it.
/// </summary>
/// <remarks>
/// Built in exactly one place, deliberately. The path used to be spelled out by every caller and they
/// disagreed - a repair wrote the project's id while an install echoed back whatever handle the user had
/// typed - so the two rewrote each other's files on every run and the diagnosis called a working
/// configuration stale.
/// </remarks>
public static class ProjectMcpEndpoint
{
    /// <summary>The path prefix every project endpoint shares.</summary>
    public const string PathPrefix = "/mcp/projects/";

    /// <summary>
    /// The endpoint of <paramref name="project"/> on the daemon listening at
    /// <paramref name="daemonBaseAddress"/>, for example <c>http://127.0.0.1:24598</c>.
    /// </summary>
    /// <remarks>
    /// The project's <see cref="RegisteredProject.Handle"/> goes into the path, so the URL a person reads in
    /// an agent's configuration says which project it belongs to. That handle is editable, so renaming a
    /// project moves its endpoint; the diagnosis then reports the stale files and a repair rewrites them.
    /// </remarks>
    public static string For(string daemonBaseAddress, RegisteredProject project) =>
        $"{daemonBaseAddress.TrimEnd('/')}{PathPrefix}{Uri.EscapeDataString(project.Handle)}";
}
