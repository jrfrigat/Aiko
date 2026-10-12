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
    // The gates of the keys calls are holding right now, one entry per key in flight rather than one per key
    // that ever was: an entry leaves the table when the last of its holders lets go.
    private readonly Dictionary<string, GateEntry> _gates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gatesLock = new(1, 1);

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

        try
        {
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject;
            return document?["result"]?.GetValue<string>();
        }
        catch (JsonException exception)
        {
            // A record nobody can read is a broken state. Answering it as "unknown" would run a mutating call
            // a second time - the very thing these records exist to prevent - so say where the file is and let
            // somebody look at it instead.
            throw new InvalidOperationException(
                $"The idempotency record '{path}' cannot be read. Delete it to let the call run again.",
                exception);
        }
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

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> EnterAsync(
        string scope,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var identity = Identity(scope, requestId);
        GateEntry entry;
        await _gatesLock.WaitAsync(cancellationToken);
        try
        {
            if (!_gates.TryGetValue(identity, out entry!))
            {
                entry = new GateEntry();
                _gates[identity] = entry;
            }

            // Counted under the table's lock, so a holder can never be holding an entry that was taken out.
            entry.Holders++;
        }
        finally
        {
            _gatesLock.Release();
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
        }
        catch
        {
            await LeaveAsync(identity, entry);
            throw;
        }

        return new Gate(this, identity, entry);
    }

    /// <summary>
    /// Lets go of one holder of a key: the gate is handed to the next caller, and the entry leaves the table
    /// when this was the last holder of it.
    /// </summary>
    private async ValueTask LeaveAsync(string identity, GateEntry entry)
    {
        entry.Gate.Release();
        await _gatesLock.WaitAsync();
        try
        {
            entry.Holders--;
            if (entry.Holders == 0)
            {
                _gates.Remove(identity);
            }
        }
        finally
        {
            _gatesLock.Release();
        }
    }

    /// <summary>One key's gate, and how many calls are holding or waiting for it.</summary>
    private sealed class GateEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int Holders { get; set; }
    }

    /// <summary>
    /// A held gate: letting go of it is what lets the next call with this key in, or lets the entry go.
    /// </summary>
    private sealed class Gate(FileIdempotencyStore store, string identity, GateEntry entry) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            // A caller that disposes twice releases one holder once: the count is what keeps the table honest.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                await store.LeaveAsync(identity, entry);
            }
        }
    }

    /// <summary>
    /// What names one key: the scope it belongs to and the caller's own value for it.
    /// </summary>
    private static string Identity(string scope, string requestId) => $"{scope}\n{requestId}";

    /// <summary>
    /// Where one key's record lives: a hash of the scope and the key under the daemon's idempotency root.
    /// </summary>
    private string ResolvePath(string scope, string requestId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}\n{requestId}"));
        return Path.Combine(paths.IdempotencyRoot, $"{Convert.ToHexStringLower(digest)}.json");
    }
}
