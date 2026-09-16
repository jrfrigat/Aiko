namespace Aiko.Infrastructure.Threading;

/// <summary>
/// Reference-counted keyed async locks: serializes work per key and drops a key's
/// semaphore once its last holder releases it, so idle keys never leak in a
/// long-lived daemon.
/// </summary>
internal sealed class KeyedLockStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Acquires the lock for <paramref name="key"/> and returns a releaser
    /// that must be disposed to release the lock and the reference.
    /// </summary>
    public async ValueTask<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = AcquireReference(key);
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            DropReference(key, semaphore);
            throw;
        }

        return new Releaser(this, key, semaphore);
    }

    private SemaphoreSlim AcquireReference(string key)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Semaphore;
            }

            var semaphore = new SemaphoreSlim(1, 1);
            _entries[key] = new Entry(semaphore, 1);
            return semaphore;
        }
    }

    private void ReleaseLock(string key, SemaphoreSlim semaphore)
    {
        semaphore.Release();
        DropReference(key, semaphore);
    }

    private void DropReference(string key, SemaphoreSlim semaphore)
    {
        var dispose = false;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry) &&
                ReferenceEquals(entry.Semaphore, semaphore) &&
                --entry.RefCount <= 0)
            {
                _entries.Remove(key);
                dispose = true;
            }
        }

        if (dispose)
        {
            semaphore.Dispose();
        }
    }

    private sealed class Entry(SemaphoreSlim semaphore, int refCount)
    {
        public SemaphoreSlim Semaphore { get; } = semaphore;

        public int RefCount { get; set; } = refCount;
    }

    private sealed class Releaser(KeyedLockStore store, string key, SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => store.ReleaseLock(key, semaphore);
    }
}
