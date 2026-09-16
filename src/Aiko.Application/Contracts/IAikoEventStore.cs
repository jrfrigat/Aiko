namespace Aiko.Application.Contracts;

/// <summary>
/// Read access to the append-only event journal in id order.
/// </summary>
public interface IAikoEventStore
{
    /// <summary>
    /// Reads up to <paramref name="limit"/> events of the project with id greater than
    /// <paramref name="afterId"/>, in ascending id order.
    /// </summary>
    ValueTask<IReadOnlyList<AikoEvent>> ReadAsync(
        string projectId,
        long afterId,
        int limit,
        CancellationToken cancellationToken);
}
