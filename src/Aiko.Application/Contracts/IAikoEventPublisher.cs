namespace Aiko.Application.Contracts;

/// <summary>
/// Publishes project change events: persists them in the append-only store
/// (assigning the sequential id) and broadcasts them to live subscribers.
/// </summary>
public interface IAikoEventPublisher
{
    /// <summary>
    /// Publishes an event of <paramref name="type"/> with a JSON payload
    /// and returns the persisted event with its assigned id.
    /// </summary>
    ValueTask<AikoEvent> PublishAsync(
        string projectId,
        string type,
        string payloadJson,
        CancellationToken cancellationToken);
}
