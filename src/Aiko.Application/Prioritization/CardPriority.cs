using Aiko.Domain.Prioritization;

namespace Aiko.Application.Prioritization;

/// <summary>
/// Effective priority of a card on the board: the card identifier
/// plus its computed <see cref="PrioritySnapshot"/>.
/// </summary>
public sealed record CardPriority(string CardId, PrioritySnapshot Snapshot);
