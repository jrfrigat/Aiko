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
/// A document an agent proposes for the durable project memory after finishing a stage, waiting beside
/// the card that produced it for a person to approve or reject.
/// </summary>
/// <remarks>
/// A proposal is not memory. Until a person approves it, nothing is written under <c>.aiko/memory</c>: the
/// candidate is a decision waiting to be made, and <see cref="Status"/> is where it stands.
/// </remarks>
/// <param name="Id">Identifier of the proposal, unique within the card.</param>
/// <param name="CardId">The card whose stage produced it.</param>
/// <param name="Path">Relative Markdown path the document would take inside the memory.</param>
/// <param name="Content">The Markdown the agent proposes to store.</param>
/// <param name="Rationale">Why the agent thinks this belongs in the memory.</param>
/// <param name="Author">Who proposed it, for example the agent adapter id.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="Status">Where the decision stands.</param>
/// <param name="ResolvedAt">When it was decided, or null while it waits.</param>
/// <param name="ResolvedBy">Who decided, or null while it waits.</param>
/// <param name="DecisionNote">What the decider said, or null.</param>
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
