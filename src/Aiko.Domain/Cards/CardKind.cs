using System.Text.Json.Serialization;

namespace Aiko.Domain.Cards;

/// <summary>
/// Kind of a card on the project board.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardKind>))]
public enum CardKind
{
    /// <summary>
    /// User story: a large functional requirement decomposed into child tasks.
    /// </summary>
    Story,

    /// <summary>
    /// Atomic task executed by an agent within a single workflow stage.
    /// </summary>
    Task
}
