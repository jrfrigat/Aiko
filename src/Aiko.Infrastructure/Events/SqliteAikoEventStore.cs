using System.Globalization;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Events;

/// <summary>
/// Reads the append-only event journal from SQLite in ascending id order.
/// </summary>
public sealed class SqliteAikoEventStore(AikoDatabase database, IProjectCatalog projects) : IAikoEventStore
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AikoEvent>> ReadAsync(
        string projectId,
        long afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // Events are journaled under the project's immutable id; a subscriber may hold its readable handle.
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, type, payload_json, occurred_utc
            FROM events
            WHERE project_id = $projectId AND id > $afterId
            ORDER BY id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$projectId", project.Id);
        command.Parameters.AddWithValue("$afterId", afterId);
        command.Parameters.AddWithValue("$limit", limit);

        var events = new List<AikoEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AikoEvent(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.GetString(3)));
        }

        return events;
    }
}
