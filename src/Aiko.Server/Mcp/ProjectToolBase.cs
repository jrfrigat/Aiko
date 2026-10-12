using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// Shared plumbing for the Aiko MCP tools: resolves the project from the projectId
/// route value of the /mcp/projects/{projectId} endpoint and parses tool arguments.
/// </summary>
internal abstract class ProjectToolBase(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    // A tool that mutates passes the store, so a repeated call can be answered from its record instead of
    // doing the work twice. A tool that only reads leaves it out - and a tool that asks for a key without a
    // store is a programming error rather than a silent downgrade of the guarantee.
    IIdempotencyStore? idempotency = null)
{
    /// <summary>
    /// Access to the current HTTP context carrying the projectId route value.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    /// <summary>
    /// The registry of projects, so a tool can resolve a neighbour named by id or handle - for example a
    /// linked project a card or a note is read from or written to.
    /// </summary>
    protected IProjectCatalog ProjectCatalog { get; } = projects;

    /// <summary>
    /// Resolves the identifier of the current project from the MCP route.
    /// </summary>
    protected string GetProjectId()
    {
        var value = HttpContextAccessor.HttpContext?.Request.RouteValues["projectId"]?.ToString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("The MCP endpoint has no projectId route value.")
            : value;
    }

    /// <summary>
    /// Loads the registered project of the current MCP route.
    /// </summary>
    protected async ValueTask<RegisteredProject> GetProjectAsync(
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        return await ProjectCatalog.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
    }

    /// <summary>
    /// Parses the card type argument. Any type the project defines is accepted - a project may add its own
    /// types in the workflow editor - so the value is only canonicalised, not checked against a fixed list.
    /// </summary>
    protected static string ParseCardKind(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Card kind is required.", nameof(value))
            : CardKind.Canonical(value);

    /// <summary>
    /// Parses an agent attempt state argument, for example "rate-limited".
    /// </summary>
    protected static AgentAttemptState ParseAgentState(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "queued" => AgentAttemptState.Queued,
            "running" => AgentAttemptState.Running,
            "waiting-for-user" => AgentAttemptState.WaitingForUser,
            "rate-limited" => AgentAttemptState.RateLimited,
            "paused" => AgentAttemptState.Paused,
            "failed" => AgentAttemptState.Failed,
            "cancelled" => AgentAttemptState.Cancelled,
            "superseded" => AgentAttemptState.Superseded,
            "completed" => AgentAttemptState.Completed,
            _ => throw new ArgumentException($"Unknown agent state: {value}", nameof(value))
        };

    /// <summary>
    /// Serializes a stage execution with the server JSON contract.
    /// </summary>
    protected static string SerializeExecution(StageExecution execution) =>
        JsonSerializer.Serialize(execution, ServerJsonContext.Default.StageExecution);

    /// <summary>
    /// The result an earlier call of this tool returned for this key, or null when the call is new - and
    /// null as well when the caller sent no key, which is how a call that will not be repeated is made.
    /// </summary>
    protected ValueTask<string?> RecallAsync(
        string tool,
        string? requestId,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(requestId)
            ? ValueTask.FromResult<string?>(null)
            : Store().ReadAsync(Scope(tool), requestId.Trim(), cancellationToken);

    /// <summary>
    /// Records the result of a call that ran, so a repeat of it can be answered from the record instead of
    /// doing the work a second time. Called after the work returned: a call that threw is not remembered.
    /// </summary>
    protected ValueTask RememberAsync(
        string tool,
        string? requestId,
        string result,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(requestId)
            ? ValueTask.CompletedTask
            : Store().WriteAsync(Scope(tool), requestId.Trim(), result, cancellationToken);

    /// <summary>
    /// The tool and the project a key belongs to: the same key in another tool, or in another project,
    /// names another call.
    /// </summary>
    private string Scope(string tool) => $"{tool}|{GetProjectId()}";

    /// <summary>
    /// The store, or a refusal: a tool that offers a key but cannot remember it would answer a repeat by
    /// doing the work again, which is worse than saying so.
    /// </summary>
    private IIdempotencyStore Store() =>
        idempotency ?? throw new InvalidOperationException(
            "This tool was built without an idempotency store, so it cannot answer a repeated call from a key.");
}
