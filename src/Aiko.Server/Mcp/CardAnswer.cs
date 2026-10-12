using System.Text.Json;
using System.Text.Json.Nodes;
using Aiko.Domain.Cards;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// The answer a card creation gives back: the whole card, or - by default - the few fields an agent needs
/// to take its next step.
/// </summary>
/// <remarks>
/// A created card used to come back whole, its texts and all, and echoing a freshly written card says back
/// what the caller just sent. At a breakdown of dozens of cards that echo is what a session's budget goes
/// on. What an agent does next with a new card is link it to its parent and estimate it, and both need the
/// id and the revision rather than the requirements it has just written. The whole card stays one call away
/// with <c>aiko_get_card</c>, and a caller that wants it immediately asks for it with <c>fullCard</c>.
/// </remarks>
internal static class CardAnswer
{
    /// <summary>
    /// The fields a compact answer carries: the whole of the contract. This is a list of what an agent needs
    /// after creating a card, so it is meant to be extended deliberately - a field added to
    /// <see cref="Card"/> later does not silently make every creation answer heavier.
    /// </summary>
    private static readonly string[] CompactKeys =
        ["reference", "kind", "title", "workflowId", "stageId", "revision"];

    /// <summary>
    /// Answers a creation with the compact card, or with the whole one when <paramref name="fullCard"/> is set.
    /// </summary>
    /// <param name="card">The card that was just written.</param>
    /// <param name="fullCard">True to answer with the whole card as before.</param>
    public static string For(Card card, bool fullCard)
    {
        var cardNode = JsonSerializer.SerializeToNode(card, ServerJsonContext.Default.Card)!
            .AsObject();
        if (fullCard)
        {
            return cardNode.ToJsonString();
        }

        var compact = new JsonObject();
        foreach (var key in CompactKeys)
        {
            if (cardNode.TryGetPropertyValue(key, out var value))
            {
                // DeepClone because a node already held by the card's object cannot move to another parent.
                compact[key] = value?.DeepClone();
            }
        }

        return compact.ToJsonString();
    }
}
