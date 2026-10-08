using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Memory;

/// <summary>One indexed chunk: where it came from and the vector computed for it.</summary>
internal readonly record struct IndexedChunk(string Path, int ChunkIndex, string Content, float[] Vector);

/// <summary>
/// The vector projection of the project memory, kept in the same SQLite database as the FTS index and beside it.
/// </summary>
/// <remarks>
/// Derived state, exactly like <c>memory_fts</c>: the canonical memory is the Markdown under
/// <c>.aiko/memory</c>, and a vector is a number Aiko computed from it. Nothing here is edited by hand, nothing
/// here is worth backing up, and a reindex throws all of it away and rebuilds it - which is also why it does not
/// live under <c>.aiko</c> where it would be mistaken for the user's own data.
/// </remarks>
internal sealed class SqliteMemoryVectorIndex(AikoDatabase database)
{
    /// <summary>
    /// Replaces a project's vectors in one transaction: what was there is gone, whether or not anything new
    /// follows. This is what makes "the index is rebuilt from the files" true rather than "added to".
    /// </summary>
    public async ValueTask ReplaceAsync(
        string projectId,
        string modelId,
        int dimensions,
        IReadOnlyList<IndexedChunk> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM memory_vectors WHERE project_id = $projectId;";
            clear.Parameters.AddWithValue("$projectId", projectId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var updatedUtc = DateTimeOffset.UtcNow.ToString("O");
        foreach (var chunk in chunks)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO memory_vectors(
                    project_id, path, chunk_index, content, content_hash, model_id, dimensions, vector, updated_utc)
                VALUES (
                    $projectId, $path, $chunkIndex, $content, $contentHash, $modelId, $dimensions, $vector, $updatedUtc);
                """;
            insert.Parameters.AddWithValue("$projectId", projectId);
            insert.Parameters.AddWithValue("$path", chunk.Path);
            insert.Parameters.AddWithValue("$chunkIndex", chunk.ChunkIndex);
            insert.Parameters.AddWithValue("$content", chunk.Content);
            insert.Parameters.AddWithValue("$contentHash", ContentHash(chunk.Content));
            insert.Parameters.AddWithValue("$modelId", modelId);
            insert.Parameters.AddWithValue("$dimensions", dimensions);
            insert.Parameters.AddWithValue("$vector", Encode(chunk.Vector));
            insert.Parameters.AddWithValue("$updatedUtc", updatedUtc);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Drops a project's vectors, which is what turning the module off has to leave behind.</summary>
    public async ValueTask ClearAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memory_vectors WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads a project's vectors for one model. Vectors of a different model are left out on purpose: two models
    /// have two spaces, and ranking a query's vector against another model's vector would compare numbers that
    /// look alike and mean nothing alike.
    /// </summary>
    public async ValueTask<IReadOnlyList<IndexedChunk>> LoadAsync(
        string projectId,
        string modelId,
        CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT path, chunk_index, content, vector
            FROM memory_vectors
            WHERE project_id = $projectId AND model_id = $modelId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$modelId", modelId);

        var chunks = new List<IndexedChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new IndexedChunk(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                Decode((byte[])reader.GetValue(3))));
        }

        return chunks;
    }

    /// <summary>Writes a vector as little-endian float32, the layout SQLite hands back as a BLOB.</summary>
    private static byte[] Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] Decode(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    /// <summary>Short stable hash of a chunk's text, so a row can say which revision it was computed from.</summary>
    private static string ContentHash(string content) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));
}
