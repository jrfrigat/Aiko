namespace Aiko.Application.Contracts;

/// <summary>Remembers the result of a mutating tool call under a key the caller supplies, so a call repeated
/// after its answer was lost returns the first result instead of doing the work a second time.</summary>
/// <remarks>
/// The key is the caller's, not Aiko's: only the caller knows a second call is the same call, so a call that
/// arrives without one is not remembered at all.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>The result recorded for this key, or null when the call is new. A key is scoped to the calling
    /// tool and the project it writes to.</summary>
    ValueTask<string?> ReadAsync(string scope, string requestId, CancellationToken cancellationToken);

    /// <summary>Records the result of a call that ran, so a repeat of that call is answered from here.</summary>
    ValueTask WriteAsync(string scope, string requestId, string result, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the gate for one key, so two calls with the same key run one after the other instead of both
    /// doing the work. The caller holds it for the whole call and releases it by disposing what this returns;
    /// taken before the record is read, it is what makes the second call read the record rather than run
    /// again, and a call that throws releases it without writing, so the next attempt runs.
    /// </summary>
    ValueTask<IAsyncDisposable> EnterAsync(string scope, string requestId, CancellationToken cancellationToken);
}

/// <summary>
/// The gate of a call made without a key: nothing to take and nothing to release, so a caller given no key can
/// still hold and dispose one without branching.
/// </summary>
public static class NoIdempotencyGate
{
    /// <summary>The gate itself.</summary>
    public static IAsyncDisposable Instance { get; } = new Gate();

    private sealed class Gate : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
