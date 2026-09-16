using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Events;

/// <summary>
/// Publishes events into the append-only SQLite journal (assigning the sequential id)
/// and then broadcasts them to live subscribers.
/// </summary>
public sealed class SqliteAikoEventPublisher(
    AikoDatabase database,
    AikoEventBroadcaster broadcaster) : IAikoEventPublisher
{
    /// <inheritdoc />
    public async ValueTask<AikoEvent> PublishAsync(
        string projectId,
        string type,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var occurredAt = DateTimeOffset.UtcNow;
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO events(project_id, type, payload_json, occurred_utc)
            VALUES ($projectId, $type, $payloadJson, $occurredUtc);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$occurredUtc", occurredAt.ToString("O"));
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The event insert returned no id."));

        var @event = new AikoEvent(id, projectId, type, occurredAt, payloadJson);
        broadcaster.Broadcast(@event);
        return @event;
    }
}
