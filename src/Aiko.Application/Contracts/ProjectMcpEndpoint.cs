namespace Aiko.Application.Contracts;

/// <summary>
/// The MCP endpoint of one project: the single address an agent's configuration may carry for it, built in
/// exactly one place - the path used to be spelled out by every caller, they disagreed, and the two rewrote
/// each other's files on every run.
/// </summary>
public static class ProjectMcpEndpoint
{
    /// <summary>The path prefix every project endpoint shares.</summary>
    public const string PathPrefix = "/mcp/projects/";

    /// <summary>The endpoint of <paramref name="project"/> on the daemon listening at
    /// <paramref name="daemonBaseAddress"/>, for example <c>http://127.0.0.1:24598</c>. The project's handle
    /// goes into the path, so the URL a person reads says which project it belongs to; that handle is editable,
    /// so renaming a project moves its endpoint.</summary>
    public static string For(string daemonBaseAddress, RegisteredProject project) =>
        $"{daemonBaseAddress.TrimEnd('/')}{PathPrefix}{Uri.EscapeDataString(project.Handle)}";
}
