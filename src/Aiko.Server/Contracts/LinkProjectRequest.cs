namespace Aiko.Server.Contracts;

/// <summary>
/// Links a project to this one, or replaces what an existing link says.
/// </summary>
/// <param name="Description">
/// What the linked project is for, in the words of whoever links it. The description is what an agent reads
/// when it decides whether a piece of work belongs to that project, so an empty one is refused rather than
/// stored: a link nobody can explain is a link nobody will use.
/// </param>
/// <param name="Reference">
/// Where that project's reference lives - a path or an address to its documentation, API index or entry point -
/// or null. Worded for any neighbour, because a link may point at a library or at an application.
/// </param>
/// <param name="WhenToUse">When a piece of work belongs to that project, or null.</param>
/// <param name="WhenNotToUse">When it belongs here instead, or null.</param>
internal sealed record LinkProjectRequest(
    string Description,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);
