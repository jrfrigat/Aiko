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
}
