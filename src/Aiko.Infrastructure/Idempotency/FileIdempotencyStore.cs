using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Idempotency;

/// <summary>
/// Idempotency records kept as one file per key, in the daemon's own data directory.
/// </summary>
/// <remarks>
/// The file name is a hash of the scope and the key, so nothing a caller sends reaches a path. The record
/// names both of them back for whoever reads the directory, and holds the result exactly as the tool
/// answered it, so a repeat answers with the same bytes rather than a fresh rendering of a moved-on world.
/// </remarks>
public sealed class FileIdempotencyStore(AikoDataPaths paths) : IIdempotencyStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public async ValueTask<string?> ReadAsync(
        string scope,
        string requestId,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(scope, requestId);
        if (!File.Exists(path))
        {
            return null;
        }

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject;
        return document?["result"]?.GetValue<string>();
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        string scope,
        string requestId,
        string result,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var path = ResolvePath(scope, requestId);
        var record = new JsonObject
        {
            ["schemaVersion"] = CurrentSchemaVersion,
            ["scope"] = scope,
            ["requestId"] = requestId,
            ["recordedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["result"] = result
        };
        var text = record.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, text, cancellationToken);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Where one key's record lives: a hash of the scope and the key under the daemon's idempotency root.
    /// </summary>
    private string ResolvePath(string scope, string requestId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}\n{requestId}"));
        return Path.Combine(paths.IdempotencyRoot, $"{Convert.ToHexStringLower(digest)}.json");
    }
}
