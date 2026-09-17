using System.Diagnostics;
using System.Globalization;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The daemon's own runs, recorded in SQLite and reported from the running process.
/// </summary>
/// <remarks>
/// The installation's restarts and crashes are exactly the kind of information that is fine to lose: it
/// exists to answer "is this daemon healthy", and the answer comes back the next time it starts. The
/// process metrics are read live from this process rather than stored.
/// </remarks>
public sealed class SqliteDaemonTelemetry(AikoDatabase database) : IDaemonTelemetry
{
    private DateTimeOffset _startedAt;
    private long _runId;

    /// <inheritdoc />
    public async ValueTask<DaemonTelemetry> StartAsync(CancellationToken cancellationToken)
    {
        _startedAt = DateTimeOffset.UtcNow;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // A run that never recorded a clean stop is a crash: the row it left behind is the evidence.
        command.CommandText =
            """
            INSERT INTO daemon_runs(started_utc) VALUES ($startedUtc);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$startedUtc", _startedAt.ToString("O", CultureInfo.InvariantCulture));
        _runId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        return await ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_runId == 0)
        {
            return;
        }

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE daemon_runs SET stopped_utc = $stoppedUtc WHERE id = $id;";
        command.Parameters.AddWithValue("$stoppedUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", _runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<DaemonTelemetry> ReadAsync(CancellationToken cancellationToken)
    {
        var runs = 0;
        var crashes = 0;

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*),
                       SUM(CASE WHEN stopped_utc IS NULL AND id <> $currentId THEN 1 ELSE 0 END)
                FROM daemon_runs
                """;
            command.Parameters.AddWithValue("$currentId", _runId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                runs = reader.GetInt32(0);
                crashes = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            }
        }

        using var process = Process.GetCurrentProcess();
        var startedAt = _startedAt == default ? process.StartTime.ToUniversalTime() : _startedAt;
        return new DaemonTelemetry(
            startedAt,
            DateTimeOffset.UtcNow - startedAt,
            runs,
            crashes,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false));
    }
}
