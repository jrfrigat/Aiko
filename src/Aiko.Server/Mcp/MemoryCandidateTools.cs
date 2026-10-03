using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools over the queue of memory proposals: an agent proposes a document for the durable memory,
/// and a person approves or rejects it. Nothing reaches <c>.aiko/memory</c> until it is approved.
/// </summary>
[McpServerToolType]
internal sealed class MemoryCandidateTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IMemoryCandidateStore candidates) : ProjectToolBase(httpContextAccessor, projects)
{
    /// <summary>
    /// Who a proposal is attributed to. The tool has no adapter id to name - the signatures a card fixes
    /// carry none - so a proposal says only that an agent made it.
    /// </summary>
    private const string ProposerAuthor = "agent";

    /// <summary>
    /// Who a decision is attributed to. Resolving is the person's decision that the call carries out, not
    /// the agent's own.
    /// </summary>
    private const string DecisionBy = "user";

    [McpServerTool(Name = "aiko_propose_memory", Title = "Propose Aiko memory")]
    [Description(
        "Proposes a document for the durable project memory after finishing a stage. It waits beside the " +
        "card until a person approves it; nothing is stored before that.")]
    public async Task<string> ProposeMemoryAsync(
        [Description("The card whose stage produced it.")]
        string cardId,
        [Description("Relative Markdown path the document would take inside the memory.")]
        string path,
        [Description("Complete Markdown content to propose.")]
        string content,
        [Description("Why this belongs in the memory.")]
        string rationale,
        CancellationToken cancellationToken)
    {
        var candidate = await candidates.ProposeAsync(
            GetProjectId(),
            cardId,
            path,
            content,
            rationale,
            ProposerAuthor,
            cancellationToken);
        return JsonSerializer.Serialize(candidate, ServerJsonContext.Default.MemoryCandidate);
    }

    [McpServerTool(Name = "aiko_list_memory_candidates", Title = "List Aiko memory candidates")]
    [Description("Lists what waits for a decision, optionally for one card. Get project context first.")]
    public async Task<string> ListMemoryCandidatesAsync(
        [Description("Only this card's candidates, or null for the whole project.")]
        string? cardId,
        CancellationToken cancellationToken)
    {
        var list = await candidates.ListAsync(GetProjectId(), cardId, cancellationToken);
        return JsonSerializer.Serialize(list, ServerJsonContext.Default.IReadOnlyListMemoryCandidate);
    }

    [McpServerTool(Name = "aiko_resolve_memory_candidate", Title = "Resolve Aiko memory candidate")]
    [Description(
        "Approves or rejects a pending memory proposal. Approving writes the document into the durable " +
        "memory through the same store as aiko_store_memory; rejecting writes nothing. The decision is the " +
        "person's.")]
    public async Task<string> ResolveMemoryCandidateAsync(
        [Description("The card the candidate belongs to.")]
        string cardId,
        [Description("The candidate id.")]
        string candidateId,
        [Description("approve or reject.")]
        string decision,
        [Description("What the decider said, or null.")]
        string? note,
        CancellationToken cancellationToken)
    {
        var approve = decision.Trim().ToLowerInvariant() switch
        {
            "approve" or "approved" => true,
            "reject" or "rejected" => false,
            _ => throw new ArgumentException(
                $"Unknown decision: {decision}. Use approve or reject.",
                nameof(decision))
        };

        var resolved = approve
            ? await candidates.ApproveAsync(
                GetProjectId(), cardId, candidateId, DecisionBy, note, cancellationToken)
            : await candidates.RejectAsync(
                GetProjectId(), cardId, candidateId, DecisionBy, note, cancellationToken);
        return JsonSerializer.Serialize(resolved, ServerJsonContext.Default.MemoryCandidate);
    }
}
