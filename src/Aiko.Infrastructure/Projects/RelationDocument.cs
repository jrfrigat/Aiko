using Aiko.Domain.Cards;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// The .aiko/relations.json document: all project relations with an overall revision.
/// </summary>
public sealed record RelationDocument(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<CardRelation> Relations);
