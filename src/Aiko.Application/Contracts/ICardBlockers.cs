using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Reads the cards that block a card: the incoming <c>blocks</c> edges, resolved against the project's cards
/// and workflows so a blocker can be named back to the user.
/// </summary>
/// <remarks>
/// A port of its own rather than another method on a store: answering the question needs three sources -
/// relations, cards and workflows - and none of those stores owns the other two. The rule itself lives in
/// <see cref="CardBlocking"/>; this is only the reading around it, kept in one place so every caller that gates
/// work asks the same question and gets the same answer.
/// </remarks>
public interface ICardBlockers
{
    /// <summary>
    /// The cards that block <paramref name="card"/> and whose work is not finished yet; empty when the card is
    /// free to be worked. The project may be addressed by its id or by its readable handle.
    /// </summary>
    ValueTask<IReadOnlyList<CardBlocker>> UnfinishedAsync(
        CardReference card,
        CancellationToken cancellationToken);
}
