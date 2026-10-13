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
    /// Answers with the compact card, or with the whole one when <paramref name="fullCard"/> is set.
    /// </summary>
    /// <param name="card">The card that was just written.</param>
    /// <param name="fullCard">True to answer with the whole card as before.</param>
    /// <param name="keys">
    /// The fields a compact answer carries, when the tool needs a different set from the default: a write tool
    /// that changed the size and the scores says so, while one that only moved the card says where it went.
    /// Left empty, the creating set is used.
    /// </param>
    public static string For(Card card, bool fullCard, params string[] keys)
    {
        var cardNode = JsonSerializer.SerializeToNode(card, ServerJsonContext.Default.Card)!
            .AsObject();
        if (fullCard)
        {
            return cardNode.ToJsonString();
        }

        var compact = new JsonObject();
        foreach (var key in keys.Length == 0 ? CompactKeys : keys)
        {
            if (cardNode.TryGetPropertyValue(key, out var value))
            {
                // DeepClone because a node already held by the card's object cannot move to another parent.
                compact[key] = value?.DeepClone();
            }
        }

        return compact.ToJsonString();
    }

    /// <summary>
    /// The fields a listed card carries: enough to find it, place it and decide about it, and nothing an agent
    /// would have to read a whole card for.
    /// </summary>
    /// <remarks>
    /// A list is read far more often than it is acted on, and its cost is counted in the cards it holds: the
    /// requirements of every card in a project used to ride along with a question as small as "what is on the
    /// board". A field added to <see cref="Card"/> later does not silently make every list heavier either.
    /// </remarks>
    private static readonly string[] ListKeys =
        ["reference", "kind", "title", "workflowId", "stageId", "revision", "size", "ownPriority"];

    /// <summary>Answers with the thin list, or with the whole cards when <paramref name="fullCards"/> is set.</summary>
    /// <remarks>
    /// The shape of the answer is unchanged - a bare array of cards either way - so what a reader looks up in it
    /// (<c>[].reference.cardId</c>) keeps working; only what each card says about itself changes.
    /// </remarks>
    public static string List(IReadOnlyList<Card> cards, bool fullCards)
    {
        if (fullCards)
        {
            return JsonSerializer.Serialize(cards, ServerJsonContext.Default.IReadOnlyListCard);
        }

        var thin = new JsonArray();
        foreach (var card in cards)
        {
            // The node overload is named explicitly: Add<T> would build a JsonValue by reflection, which this
            // assembly's trimming analysis refuses at build time.
            thin.Add((JsonNode)ThinNode(
                JsonSerializer.SerializeToNode(card, ServerJsonContext.Default.Card)!.AsObject()));
        }

        return thin.ToJsonString();
    }

    /// <summary>
    /// Thins the card nodes of an answer that carries more than cards - the board, where the cards sit beside its
    /// relations and its priorities. Those neighbours are left exactly as they were.
    /// </summary>
    public static JsonArray Thin(JsonArray cards)
    {
        var thin = new JsonArray();
        foreach (var card in cards)
        {
            thin.Add(card is JsonObject value ? (JsonNode)ThinNode(value) : card?.DeepClone());
        }

        return thin;
    }

    private static JsonObject ThinNode(JsonObject card)
    {
        var thin = new JsonObject();
        foreach (var key in ListKeys)
        {
            if (card.TryGetPropertyValue(key, out var value))
            {
                // DeepClone because a node already held by the card's object cannot move to another parent.
                thin[key] = value?.DeepClone();
            }
        }

        return thin;
    }
}
