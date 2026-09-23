using Microsoft.Data.Sqlite;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The local SQLite database: the path from <see cref="AikoDataPaths"/>, the projection
/// table schema and a pooled connection factory. Foreign keys and a write-lock timeout are
/// enabled for every pooled connection through the connection string; the one-off memory
/// migration to FTS5 is kept separate from the current schema.
/// </summary>
public sealed class AikoDatabase(AikoDataPaths paths)
{
    /// <summary>
    /// Full path to the database file.
    /// </summary>
    public string DatabasePath => paths.DatabasePath;

    /// <summary>
    /// Creates the schema (WAL) on first run and applies the one-off memory-to-FTS5 migration
    /// when upgrading from an older database.
    /// </summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(paths.DatabasePath)
            ?? throw new InvalidOperationException("The database path must include a directory.");
        Directory.CreateDirectory(directory);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteSchemaAsync(connection, cancellationToken);
        await MigrateProjectSlugsAsync(connection, cancellationToken);
        await MigrateMemoryToFtsAsync(connection, cancellationToken);
        await MigrateMemoryToProjectIdsAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Creates a new connection to the database in ReadWriteCreate mode with pooling.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            // Enforce foreign keys and a write-lock timeout on every pooled connection.
            // WAL is used (see InitializeAsync), so shared-cache mode - discouraged together
            // with WAL - is intentionally left off.
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async ValueTask ExecuteSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL UNIQUE,
                slug TEXT,
                updated_utc TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS card_stage_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                card_id TEXT NOT NULL,
                from_stage_id TEXT,
                to_stage_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                occurred_utc TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_card_stage_events_project
                ON card_stage_events(project_id, occurred_utc);

            CREATE TABLE IF NOT EXISTS daemon_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_utc TEXT NOT NULL,
                stopped_utc TEXT
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS cards (
                project_id TEXT NOT NULL,
                card_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                stage_id TEXT NOT NULL,
                revision INTEGER NOT NULL,
                document_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(project_id, card_id),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_cards_project_stage
            ON cards(project_id, stage_id);

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS relations (
                project_id TEXT NOT NULL,
                relation_id TEXT NOT NULL,
                source_card_id TEXT NOT NULL,
                target_card_id TEXT NOT NULL,
                type TEXT NOT NULL,
                document_json TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY(project_id, relation_id),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_relations_project_source
            ON relations(project_id, source_card_id);

            CREATE INDEX IF NOT EXISTS ix_relations_project_target
            ON relations(project_id, target_card_id);

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE TABLE IF NOT EXISTS executions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                card_id TEXT NOT NULL,
                stage_id TEXT NOT NULL,
                state TEXT NOT NULL,
                document_json TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_executions_card
            ON executions(project_id, card_id, created_utc);

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (4, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

            CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
                path,
                content,
                project_id UNINDEXED,
                updated_utc UNINDEXED);

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                occurred_utc TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_events_project_id
            ON events(project_id, id);

            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES (6, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the project slug column to a database created before slugs existed, and the unique index that
    /// keeps two projects from claiming the same readable handle.
    /// </summary>
    /// <remarks>
    /// The column is in the schema for a fresh database; this is the upgrade path for an existing one, where
    /// the old table already exists and <c>CREATE TABLE IF NOT EXISTS</c> does nothing. SQLite has no
    /// <c>ADD COLUMN IF NOT EXISTS</c>, so the column list is probed first.
    /// </remarks>
    private static async ValueTask MigrateProjectSlugsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'slug';";
            var hasSlug = (long)(await probe.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
            if (!hasSlug)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE projects ADD COLUMN slug TEXT;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var index = connection.CreateCommand())
        {
            // SQLite treats NULLs as distinct in a unique index, so every project that has no slug yet can
            // coexist while the backfill works through them.
            index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_projects_slug ON projects(slug);";
            await index.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var record = connection.CreateCommand();
        record.CommandText =
            "INSERT OR IGNORE INTO schema_migrations(version, applied_utc) " +
            "VALUES (7, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));";
        await record.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask MigrateMemoryToFtsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 5;";
            var applied = (long)(await check.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (applied > 0)
            {
                return;
            }
        }

        // The legacy memory_documents table exists only on databases that predate the FTS5
        // index; backfill it once, then drop it.
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'memory_documents';";
            var legacyExists = (long)(await probe.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
            if (legacyExists)
            {
                await using var backfill = connection.CreateCommand();
                backfill.CommandText =
                    """
                    INSERT INTO memory_fts(project_id, path, content, updated_utc)
                    SELECT project_id, path, content, updated_utc
                    FROM memory_documents;

                    DROP TABLE memory_documents;
                    """;
                await backfill.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var record = connection.CreateCommand())
        {
            record.CommandText =
                "INSERT OR IGNORE INTO schema_migrations(version, applied_utc) " +
                "VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));";
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Moves memory rows an older daemon kept under a project's handle onto the project's id, which is what
    /// the memory store and the reindex key them by now, and keeps the newest row where both names held the
    /// same document.
    /// </summary>
    /// <remarks>
    /// Runs on every start rather than once: it touches nothing when no row carries a handle, and a daemon
    /// of an older version sharing the database could write one again. Rows under a handle the project no
    /// longer has stay where they are - the files are the source of truth, and a reindex rebuilds them.
    /// </remarks>
    private static async ValueTask MigrateMemoryToProjectIdsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await using (var move = connection.CreateCommand())
        {
            move.Transaction = transaction;
            move.CommandText =
                """
                UPDATE memory_fts
                SET project_id = (SELECT id FROM projects WHERE slug = memory_fts.project_id)
                WHERE project_id IN (SELECT slug FROM projects WHERE slug IS NOT NULL AND slug <> id);

                DELETE FROM memory_fts
                WHERE rowid IN (
                    SELECT older.rowid
                    FROM memory_fts AS older
                    JOIN memory_fts AS newer
                        ON newer.project_id = older.project_id
                        AND newer.path = older.path
                        AND (newer.updated_utc > older.updated_utc
                            OR (newer.updated_utc = older.updated_utc AND newer.rowid > older.rowid)));

                INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
                VALUES (8, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
                """;
            await move.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

