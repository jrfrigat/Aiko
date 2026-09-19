namespace Aiko.Application.Contracts;

/// <summary>
/// A single change notification of a project: an append-only record persisted in SQLite
/// and broadcast live to subscribers (SSE). The payload is a JSON document whose shape
/// depends on the event type.
/// </summary>
public sealed record AikoEvent(
    long Id,
    string ProjectId,
    string Type,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson);

/// <summary>
/// Well-known Aiko event types.
/// </summary>
public static class AikoEventTypes
{
    /// <summary>
    /// A card was created or changed; the payload is the card JSON.
    /// </summary>
    public const string CardUpdated = "card.updated";

    /// <summary>
    /// The relation set of the project changed; there is no payload.
    /// </summary>
    public const string RelationsUpdated = "relations.updated";

    /// <summary>
    /// A stage execution changed; the payload is the StageExecution JSON.
    /// </summary>
    public const string ExecutionUpdated = "execution.updated";

    /// <summary>
    /// The project's command queue changed: a command was placed, taken or closed. The payload is the
    /// <see cref="Aiko.Domain.Execution.CardCommand"/> JSON.
    /// </summary>
    public const string CommandsUpdated = "commands.updated";

    /// <summary>
    /// A card was created in this project on behalf of another one. The payload names the created card and the
    /// target project; the target's own journal carries the matching <see cref="CardUpdated"/>.
    /// </summary>
    public const string CrossProjectCardCreated = "cross-project.card-created";
}
