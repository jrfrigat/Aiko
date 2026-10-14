namespace Aiko.Application.Contracts;

/// <summary>
/// Where a proposed memory document stands: it waits for a person, or one has decided.
/// </summary>
public enum MemoryCandidateStatus
{
    /// <summary>Proposed and waiting: nothing has been written into the memory.</summary>
    Pending,

    /// <summary>Approved: the document was written into the durable memory.</summary>
    Approved,

    /// <summary>Rejected: the person declined it, and the memory was left as it was.</summary>
    Rejected
}

/// <summary>
/// A document an agent proposes for the durable project memory after finishing a stage, waiting beside the
/// card that produced it for a person to approve or reject. A proposal is not memory: until a person approves
/// it nothing is written under <c>.aiko/memory</c>, and <c>Status</c> is where the decision stands.
/// </summary>
public sealed record MemoryCandidate(
    string Id,
    string CardId,
    string Path,
    string Content,
    string Rationale,
    string Author,
    DateTimeOffset ProposedAt,
    MemoryCandidateStatus Status,
    DateTimeOffset? ResolvedAt = null,
    string? ResolvedBy = null,
    string? DecisionNote = null);
