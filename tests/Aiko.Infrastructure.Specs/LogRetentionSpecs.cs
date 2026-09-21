using Aiko.Infrastructure.Events;
using Aiko.Infrastructure.Logging;
using Aiko.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// How much of its own record the daemon keeps. Both bounds exist so that a machine's data directory cannot
/// grow without limit, and neither trim is silent: a dropped log file is named by the file that replaces it,
/// and journal rows that go leave a marker in the journal.
/// </summary>
public sealed class LogRetentionSpecs
{
    [Fact]
    public void Log_rotation_keeps_the_files_it_was_told_to_and_names_the_one_it_dropped()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "daemon.log");
        var settings = new LogRetentionSettings(
            MaxFileBytes: LogRetentionSettings.MinimumFileBytes,
            MaxFiles: 3,
            JournalMaxAgeDays: 1);
        var rotation = new LogFileRotation(path, settings);
        var padding = new string('x', 4096);

        try
        {
            // Enough lines to fill and rotate the file several times, so the oldest kept generation is pushed
            // out by a rotation that then has to say what it dropped.
            for (var index = 0; index < 100; index++)
            {
                rotation.Append($"line {index} {padding}");
            }

            // MaxFiles counts the file being written, so three files and no fourth.
            Assert.Equal(3, Directory.GetFiles(directory, "daemon.log*").Length);
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(rotation.RotatedPath(1)));
            Assert.True(File.Exists(rotation.RotatedPath(2)));
            Assert.False(File.Exists(rotation.RotatedPath(3)));

            // The file that replaced the dropped one opens by naming it: a rotation nobody can see would be
            // indistinguishable from a log that quietly lost data.
            var current = File.ReadAllText(path);
            Assert.Contains("log rotated at", current, StringComparison.Ordinal);
            Assert.Contains("dropped daemon.log.2", current, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void A_log_file_below_its_bound_is_not_rotated()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "daemon.log");
        var rotation = new LogFileRotation(
            path,
            new LogRetentionSettings(MaxFileBytes: LogRetentionSettings.MinimumFileBytes));

        try
        {
            rotation.Append("one line");

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(rotation.RotatedPath(1)));
            Assert.DoesNotContain("log rotated at", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Daemon_settings_state_the_log_bounds_and_a_file_without_them_reads_as_the_defaults()
    {
        var directory = NewDirectory();
        var paths = new AikoDataPaths(Path.Combine(directory, "aiko.db"));
        var configuration = new DaemonEndpointConfiguration(paths);

        try
        {
            // Every settings file written before the section existed holds the port alone; it still reads, and
            // the bounds fall back to the shipped ones rather than to zero.
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(paths.SettingsPath, """{"port":18001}""");
            var legacy = await configuration.TryReadAsync();
            Assert.NotNull(legacy);
            Assert.Null(legacy!.Logs);
            Assert.Equal(LogRetentionSettings.Default, legacy.EffectiveLogs);

            // What the file states wins, and it survives the round trip through the writer.
            var chosen = new LogRetentionSettings(MaxFileBytes: 131072, MaxFiles: 3, JournalMaxAgeDays: 7);
            await configuration.SaveAsync(new DaemonEndpointSettings(18001, chosen));

            var stated = await configuration.TryReadAsync();
            Assert.NotNull(stated);
            Assert.Equal(chosen, stated!.EffectiveLogs);
            Assert.Equal(18001, stated.Port);

            // A bound that would keep no history at all is refused rather than written and then obeyed.
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await configuration.SaveAsync(
                    new DaemonEndpointSettings(18001, chosen with { MaxFiles = 1 })));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Event_journal_retention_removes_old_rows_and_leaves_a_marker_behind()
    {
        var directory = NewDirectory();
        var database = new AikoDatabase(new AikoDataPaths(Path.Combine(directory, "aiko.db")));
        var retention = new EventJournalRetention(
            database,
            new SqliteAikoEventPublisher(database, new AikoEventBroadcaster()));

        try
        {
            await database.InitializeAsync();
            await using (var connection = database.CreateConnection())
            {
                await connection.OpenAsync();
                await using var project = connection.CreateCommand();
                project.CommandText =
                    """
                    INSERT INTO projects(id, name, root_path, slug, updated_utc)
                    VALUES ('project-1', 'Project', 'C:\projects\project-1', 'project-1', '2026-01-01T00:00:00.000Z');
                    """;
                await project.ExecuteNonQueryAsync();
            }

            await InsertEventAsync(database, "old-1", DateTimeOffset.UtcNow.AddDays(-5));
            await InsertEventAsync(database, "old-2", DateTimeOffset.UtcNow.AddDays(-4));
            await InsertEventAsync(database, "fresh", DateTimeOffset.UtcNow);

            var trim = await retention.TrimAsync(
                new LogRetentionSettings(JournalMaxAgeDays: 1),
                CancellationToken.None);

            Assert.True(trim.Trimmed);
            Assert.Equal(2, trim.Removed);
            Assert.Equal(1, trim.Projects);

            var types = await ReadEventTypesAsync(database);
            // The rows past the cutoff are gone, the one inside it stayed, and the removal is stated in the
            // journal itself, so a replay that finds the gap can point at the reason for it.
            Assert.Contains("fresh", types, StringComparer.Ordinal);
            Assert.DoesNotContain("old-1", types, StringComparer.Ordinal);
            Assert.DoesNotContain("old-2", types, StringComparer.Ordinal);
            Assert.Contains(EventJournalRetention.TrimmedEventType, types, StringComparer.Ordinal);

            // Trimming again finds nothing: the marker is not itself past the cutoff, so it neither counts
            // nor goes.
            var again = await retention.TrimAsync(
                new LogRetentionSettings(JournalMaxAgeDays: 1),
                CancellationToken.None);
            Assert.False(again.Trimmed);
            Assert.Contains(
                EventJournalRetention.TrimmedEventType,
                await ReadEventTypesAsync(database),
                StringComparer.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static async Task InsertEventAsync(
        AikoDatabase database,
        string type,
        DateTimeOffset occurredAt)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO events(project_id, type, payload_json, occurred_utc)
            VALUES ('project-1', $type, '{}', $occurredUtc);
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue(
            "$occurredUtc",
            occurredAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadEventTypesAsync(AikoDatabase database)
    {
        var types = new List<string>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM events ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            types.Add(reader.GetString(0));
        }

        return types;
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Removes the scratch directory. Pooled SQLite connections are dropped first: a pooled handle keeps the
    /// file open, and a delete that fails on Windows would leave the directory behind for the next run.
    /// </summary>
    private static void Delete(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
