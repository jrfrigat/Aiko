namespace Aiko.Application.Contracts;

/// <summary>
/// A project this one is linked to: the neighbour's immutable id, the handle it is addressed by, and what the
/// link is for.
/// </summary>
/// <remarks>
/// The description is the point of the record: an agent reads it before it decides whether the work in front of
/// it belongs to that project or to this one, and a person reads it in the UI to remember why the two were
/// tied together. Storing only the id would say that a link exists and nothing about when to use it.
/// <para>
/// The three texts after it are what a description cannot carry on its own: where the neighbour's reference
/// lives, and when a piece of work belongs there and when it does not. They are optional, and they are worded
/// for any neighbour - a library whose reference is a generated directory of API files, or an application whose
/// reference is its own handbook - so one registry serves both and neither has to write an essay to be useful.
/// An absent field is normal rather than incomplete: a link written before these existed carries only its
/// description.
/// </para>
/// </remarks>
/// <param name="ProjectId">Immutable id of the linked project.</param>
/// <param name="Handle">Readable handle the linked project is addressed by, kept for display and routing.</param>
/// <param name="Description">What that project is for, in the words of whoever linked it.</param>
/// <param name="CreatedAt">When the link was made.</param>
/// <param name="Reference">
/// Where that project's reference lives - a path or an address to its documentation, API index or entry point -
/// or null when nobody said.
/// </param>
/// <param name="WhenToUse">When a piece of work belongs to that project, or null when nobody said.</param>
/// <param name="WhenNotToUse">When it belongs here instead, or null when nobody said.</param>
public sealed record ProjectLink(
    string ProjectId,
    string Handle,
    string Description,
    DateTimeOffset CreatedAt,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);

/// <summary>
/// The words a link carries, as a caller hands them over: what the neighbour is for, where its reference lives,
/// and when a piece of work belongs there and when it does not.
/// </summary>
/// <remarks>
/// A parameter object rather than four strings in a row: three of them are optional and nothing but their
/// position would tell them apart, so every caller - the REST endpoint, the MCP tool, the specs - would carry
/// the risk of swapping two of them. The store normalizes the values, so blank and absent mean one thing.
/// </remarks>
/// <param name="Description">What that project is for: required, and the one text every link has.</param>
/// <param name="Reference">Where its reference lives, or null.</param>
/// <param name="WhenToUse">When work belongs there, or null.</param>
/// <param name="WhenNotToUse">When it belongs here instead, or null.</param>
public sealed record ProjectLinkText(
    string Description,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);

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
