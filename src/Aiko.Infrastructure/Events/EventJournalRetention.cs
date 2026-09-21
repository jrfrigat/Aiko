using System.Globalization;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Logging;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Events;

/// <summary>
/// Trims the append-only event journal by age, and records in the journal itself what it removed.
/// </summary>
/// <remarks>
/// The journal is what replay reads, so its rows disappear only past an age the settings name, and never in
/// silence: before a project's rows go, a marker event of that project states how many were removed and
/// over which id range. A replay that then finds a gap can point at the reason instead of at a bug.
/// </remarks>
public sealed class EventJournalRetention(AikoDatabase database, IAikoEventPublisher events)
{
    /// <summary>Type of the marker a trim leaves behind.</summary>
    /// <remarks>
    /// Spelled here rather than in <c>AikoEventTypes</c> because it is not an event a card produced: it is
    /// the journal talking about itself. A subscriber with no case for the type ignores it, exactly as it
    /// ignores any other type it does not know.
    /// </remarks>
    public const string TrimmedEventType = "journal-trimmed";

    /// <summary>
    /// Removes journal rows older than the retention and leaves one marker per affected project.
    /// </summary>
    /// <param name="settings">The retention in force.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<EventJournalTrimResult> TrimAsync(
        LogRetentionSettings settings,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.JournalMaxAgeDays);
        var cutoffText = cutoff.ToString("O", CultureInfo.InvariantCulture);

        // What would go is counted before anything is deleted, so the marker can name it.
        var candidates = new List<JournalTrimCandidate>();
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT project_id, COUNT(*), MIN(id), MAX(id)
                FROM events
                WHERE occurred_utc < $cutoff AND type <> $marker
                GROUP BY project_id;
                """;
            command.Parameters.AddWithValue("$cutoff", cutoffText);
            command.Parameters.AddWithValue("$marker", TrimmedEventType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new JournalTrimCandidate(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3)));
            }
        }

        if (candidates.Count == 0)
        {
            return new EventJournalTrimResult(0, 0, cutoff);
        }

        long removed = 0;
        foreach (var candidate in candidates)
        {
            // The marker is written first. It is the record of the removal, so the removal must not be able
            // to happen with the record of it missing.
            var payload =
                $$"""
                {"removed":{{candidate.Count}},"firstId":{{candidate.FirstId}},"lastId":{{candidate.LastId}},"cutoffUtc":"{{cutoffText}}"}
                """;
            await events.PublishAsync(candidate.ProjectId, TrimmedEventType, payload, cancellationToken);

            // The same predicate the count used, minus the markers themselves - and the row just written is
            // newer than the cutoff, so the marker survives its own trim.
            await using var connection = database.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM events
                WHERE project_id = $projectId AND occurred_utc < $cutoff AND type <> $marker;
                """;
            command.Parameters.AddWithValue("$projectId", candidate.ProjectId);
            command.Parameters.AddWithValue("$cutoff", cutoffText);
            command.Parameters.AddWithValue("$marker", TrimmedEventType);
            removed += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new EventJournalTrimResult(candidates.Count, removed, cutoff);
    }

    /// <summary>One project's rows that are past the cutoff.</summary>
    private sealed record JournalTrimCandidate(string ProjectId, long Count, long FirstId, long LastId);
}

/// <summary>
/// What one trim removed, so the caller can say it rather than log a vague success.
/// </summary>
/// <param name="Projects">Projects whose journal lost rows.</param>
/// <param name="Removed">Rows removed in total.</param>
/// <param name="CutoffUtc">Moment before which rows were removed.</param>
public sealed record EventJournalTrimResult(int Projects, long Removed, DateTimeOffset CutoffUtc)
{
    /// <summary>Whether anything was removed at all.</summary>
    public bool Trimmed => Removed > 0;
}
