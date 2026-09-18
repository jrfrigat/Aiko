namespace Aiko.Server.Contracts;

/// <summary>
/// Links a project to this one, or replaces what an existing link says.
/// </summary>
/// <param name="Description">
/// What the linked project is for, in the words of whoever links it. The description is what an agent reads
/// when it decides whether a piece of work belongs to that project, so an empty one is refused rather than
/// stored: a link nobody can explain is a link nobody will use.
/// </param>
internal sealed record LinkProjectRequest(string Description);
