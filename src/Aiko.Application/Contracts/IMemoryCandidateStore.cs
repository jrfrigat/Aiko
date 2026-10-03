namespace Aiko.Application.Contracts;

/// <summary>
/// The queue of memory proposals: documents an agent suggests for the durable project memory, waiting
/// beside the card for a person to decide.
/// </summary>
/// <remarks>
/// This is a queue of decisions, not the memory itself. The one place that writes the memory is
/// <see cref="ApproveAsync"/>, and it writes through <see cref="IMemoryStore"/>; a proposal nobody
/// approved can therefore never become knowledge. Proposals come from an agent - the daemon never
/// analyses the text and never calls a model.
/// </remarks>
public interface IMemoryCandidateStore
{
    /// <summary>
    /// Records a proposal beside the card. It is always
    /// <see cref="MemoryCandidateStatus.Pending"/>: nothing enters the memory here.
    /// </summary>
    ValueTask<MemoryCandidate> ProposeAsync(
        string projectId,
        string cardId,
        string path,
        string content,
        string rationale,
        string author,
        CancellationToken cancellationToken);

    /// <summary>
    /// The candidates of one card, or of the whole project when <paramref name="cardId"/> is null, newest
    /// first.
    /// </summary>
    ValueTask<IReadOnlyList<MemoryCandidate>> ListAsync(
        string projectId,
        string? cardId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Approves a pending candidate: the document is written into the durable memory through
    /// <see cref="IMemoryStore"/>, and the candidate is marked decided.
    /// </summary>
    /// <exception cref="InvalidOperationException">The candidate was already decided.</exception>
    ValueTask<MemoryCandidate> ApproveAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rejects a pending candidate: the memory is left untouched and the candidate is marked decided.
    /// </summary>
    /// <exception cref="InvalidOperationException">The candidate was already decided.</exception>
    ValueTask<MemoryCandidate> RejectAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken);
}
