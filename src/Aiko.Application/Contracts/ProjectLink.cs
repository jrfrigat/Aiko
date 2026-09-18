namespace Aiko.Application.Contracts;

/// <summary>
/// A project this one is linked to: the neighbour's immutable id, the handle it is addressed by, and what the
/// link is for.
/// </summary>
/// <remarks>
/// The description is the point of the record: an agent reads it before it decides whether the work in front of
/// it belongs to that project or to this one, and a person reads it in the UI to remember why the two were
/// tied together. Storing only the id would say that a link exists and nothing about when to use it.
/// </remarks>
/// <param name="ProjectId">Immutable id of the linked project.</param>
/// <param name="Handle">Readable handle the linked project is addressed by, kept for display and routing.</param>
/// <param name="Description">What that project is for, in the words of whoever linked it.</param>
/// <param name="CreatedAt">When the link was made.</param>
public sealed record ProjectLink(
    string ProjectId,
    string Handle,
    string Description,
    DateTimeOffset CreatedAt);

/// <summary>
/// The link registry of one project, as <c>.aiko/links.json</c> holds it.
/// </summary>
/// <param name="SchemaVersion">Document shape version.</param>
/// <param name="Revision">Optimistic revision, bumped on every change.</param>
/// <param name="Links">The linked projects.</param>
public sealed record ProjectLinkDocument(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<ProjectLink> Links);
