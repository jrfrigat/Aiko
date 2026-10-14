namespace Aiko.Application.Contracts;

/// <summary>
/// A project registered in the Aiko catalog. <c>Id</c> is a generated GUID and the key written into card files,
/// relations and projections, so it never changes, while <c>Slug</c> is derived from the name and editable -
/// null only on a registration written before slugs existed - and is what a URL or a link uses.
/// </summary>
public sealed record RegisteredProject(string Id, string Name, string RootPath, string? Slug = null)
{
    /// <summary>What a link should use: the slug when there is one, the id otherwise.</summary>
    public string Handle => string.IsNullOrWhiteSpace(Slug) ? Id : Slug;
}

