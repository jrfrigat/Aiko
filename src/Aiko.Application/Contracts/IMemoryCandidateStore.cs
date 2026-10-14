namespace Aiko.Application.Contracts;

/// <summary>
/// The queue of memory proposals: documents an agent suggests for the durable project memory, waiting beside
/// the card for a person to decide. This is a queue of decisions, not the memory itself - the only place that
/// writes it is <c>ApproveAsync</c>, through <see cref="IMemoryStore"/> - and proposals come from an agent,
/// because the daemon never analyses the text and never calls a model.
/// </summary>
public interface IMemoryCandidateStore
{
    /// <summary>Records a proposal beside the card, always <see cref="MemoryCandidateStatus.Pending"/>: nothing
    /// enters the memory here.</summary>
    ValueTask<MemoryCandidate> ProposeAsync(
        string projectId,
        string cardId,
        string path,
        string content,
        string rationale,
        string author,
        CancellationToken cancellationToken);

    /// <summary>The candidates of one card, or of the whole project when no card is named, newest first.</summary>
    ValueTask<IReadOnlyList<MemoryCandidate>> ListAsync(
        string projectId,
        string? cardId,
        CancellationToken cancellationToken);

    /// <summary>Approves a pending candidate: the document is written into the durable memory through
    /// <see cref="IMemoryStore"/>, and the candidate is marked decided.</summary>
    /// <exception cref="InvalidOperationException">The candidate was already decided.</exception>
    ValueTask<MemoryCandidate> ApproveAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken);

    /// <summary>Rejects a pending candidate: the memory is left untouched and the candidate is marked
    /// decided.</summary>
    /// <exception cref="InvalidOperationException">The candidate was already decided.</exception>
    ValueTask<MemoryCandidate> RejectAsync(
        string projectId,
        string cardId,
        string candidateId,
        string resolvedBy,
        string? decisionNote,
        CancellationToken cancellationToken);
}
