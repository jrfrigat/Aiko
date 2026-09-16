namespace Aiko.Domain.Cards;

/// <summary>
/// Provenance of a card created from another project. Present only on cards that were
/// reported from a different project than the one they live in.
/// </summary>
public sealed record CardOrigin(
    string ProjectId,
    string? CardId,
    string? AdapterId,
    DateTimeOffset CreatedAtUtc);