using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Reads the cards that block a card: the incoming <c>blocks</c> edges, resolved against the project's cards
/// and workflows so a blocker can be named back to the user. A port of its own rather than another method on a
/// store, because answering the question needs three sources and none of those stores owns the other two; the
/// rule itself lives in <see cref="CardBlocking"/>, and this is only the reading around it.
/// </summary>
public interface ICardBlockers
{
    /// <summary>The cards that block <paramref name="card"/> and whose work is not finished yet; empty when the
    /// card is free to be worked.</summary>
    ValueTask<IReadOnlyList<CardBlocker>> UnfinishedAsync(
        CardReference card,
        CancellationToken cancellationToken);
}
