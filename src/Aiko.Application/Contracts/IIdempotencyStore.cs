namespace Aiko.Application.Contracts;

/// <summary>
/// Remembers the result of a mutating tool call under a key the caller supplies, so a call repeated after
/// its answer was lost returns the first result instead of doing the work a second time.
/// </summary>
/// <remarks>
/// The key is the caller's, not Aiko's: only the caller knows that a second call is the same call. A call
/// that arrives without a key is not remembered at all, because a key invented here would differ on every
/// attempt and the guarantee would be an illusion.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// The result recorded for this key, or null when the call is new.
    /// </summary>
    /// <param name="scope">What the key is scoped to - the calling tool and the project it writes to.</param>
    /// <param name="requestId">The caller's key for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string?> ReadAsync(string scope, string requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the result of a call that ran, so a repeat of that call can be answered from here.
    /// </summary>
    /// <param name="scope">What the key is scoped to - the calling tool and the project it writes to.</param>
    /// <param name="requestId">The caller's key for this call.</param>
    /// <param name="result">The result the call returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteAsync(string scope, string requestId, string result, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the gate for one key, so two calls that arrive at the same time with the same key run one after
    /// the other instead of both doing the work.
    /// </summary>
    /// <remarks>
    /// The caller holds the gate for the whole call - read the record, do the work, write the record - and
    /// releases it by disposing what this returns. Taken <em>before</em> the record is read, it is what makes
    /// the second call read that record rather than run a second time. A call that throws releases the gate
    /// without writing, and the next attempt runs again.
    /// </remarks>
    /// <param name="scope">What the key is scoped to - the calling tool and the project it writes to.</param>
    /// <param name="requestId">The caller's key for this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IAsyncDisposable> EnterAsync(string scope, string requestId, CancellationToken cancellationToken);
}

/// <summary>
/// The gate of a call made without a key: nothing to take and nothing to release, so a caller that was given
/// no key can still hold and dispose a gate without branching on it.
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
