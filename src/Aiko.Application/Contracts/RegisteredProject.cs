namespace Aiko.Application.Contracts;

/// <summary>
/// A project registered in the Aiko catalog.
/// </summary>
/// <param name="Id">
/// Stable project id - a generated GUID. It is the key written into card files, relations and every agent's
/// MCP endpoint, so it never changes.
/// </param>
/// <param name="Name">Display name.</param>
/// <param name="RootPath">Project root directory.</param>
/// <param name="Slug">
/// Human-readable handle of the project, for example <c>aiko</c>, used in the UI's URLs. It is derived from
/// the name and editable, so unlike the id it may change; null only on a registration written before slugs
/// existed and not yet backfilled.
/// </param>
public sealed record RegisteredProject(string Id, string Name, string RootPath, string? Slug = null)
{
    /// <summary>
    /// What a link should use: the slug when there is one, the id otherwise. Every screen and endpoint
    /// resolves either, so a project without a slug keeps working while the backfill catches up.
    /// </summary>
    public string Handle => string.IsNullOrWhiteSpace(Slug) ? Id : Slug;
}

