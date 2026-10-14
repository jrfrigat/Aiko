namespace Aiko.Application.Contracts;

/// <summary>
/// A project this one is linked to: the neighbour's immutable id, the handle it is addressed by, and what
/// the link is for. An absent optional text is normal rather than incomplete.
/// </summary>
/// <remarks>
/// The description is what an agent reads to decide whether the work in front of it belongs to that project;
/// the optional texts add where that project's reference lives and when work belongs there.
/// </remarks>
public sealed record ProjectLink(
    string ProjectId,
    string Handle,
    string Description,
    DateTimeOffset CreatedAt,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);

/// <summary>
/// The words a link carries, as a caller hands them over: what the neighbour is for, where its reference
/// lives, and when a piece of work belongs there and when it does not. A parameter object rather than four
/// strings in a row, because three of them are optional and only position would tell them apart; the store
/// normalizes the values, so blank and absent mean one thing.
/// </summary>
public sealed record ProjectLinkText(
    string Description,
    string? Reference = null,
    string? WhenToUse = null,
    string? WhenNotToUse = null);

/// <summary>
/// The link registry of one project, as <c>.aiko/links.json</c> holds it; <c>Revision</c> is bumped on every
/// change.
/// </summary>
public sealed record ProjectLinkDocument(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<ProjectLink> Links);
