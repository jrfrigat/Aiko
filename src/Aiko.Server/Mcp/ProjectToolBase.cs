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
    /// Starts a call under the caller's key: the gate on that key is taken first, and the record an earlier
    /// call left is read inside it, because the call ahead of this one may be writing that record right now.
    /// </summary>
    /// <remarks>
    /// A call that arrives without a key gets a call with nothing to take and nothing to remember, so a tool
    /// does not have to branch on whether it was given one.
    /// </remarks>
    protected async ValueTask<IdempotentCall> BeginCallAsync(
        string tool,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return IdempotentCall.WithoutKey();
        }

        var scope = Scope(tool);
        var key = requestId.Trim();
        var gate = await Store().EnterAsync(scope, key, cancellationToken);
        try
        {
            return IdempotentCall.Under(
                gate,
                Store(),
                scope,
                key,
                await Store().ReadAsync(scope, key, cancellationToken));
        }
        catch
        {
            // The gate is released even here: a key nobody can take again would outlive the failure.
            await gate.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// One call made under a key: the gate that keeps two calls with the same key from running at once, the
    /// record an earlier call left, and the place this call's own result goes.
    /// </summary>
    /// <remarks>
    /// Held with <c>await using</c> for the whole call, so the gate outlives the work it guards. A call that
    /// throws never writes, and its gate is released on the way out.
    /// </remarks>
    protected sealed class IdempotentCall : IAsyncDisposable
    {
        private readonly IAsyncDisposable? _gate;
        private readonly IIdempotencyStore? _store;
        private readonly string? _scope;
        private readonly string? _key;

        private IdempotentCall(
            IAsyncDisposable? gate,
            IIdempotencyStore? store,
            string? scope,
            string? key,
            string? remembered)
        {
            _gate = gate;
            _store = store;
            _scope = scope;
            _key = key;
            Remembered = remembered;
        }

        /// <summary>The result an earlier call with this key returned, or null when this call is new.</summary>
        public string? Remembered { get; }

        /// <summary>A call made without a key: it holds nothing and remembers nothing.</summary>
        public static IdempotentCall WithoutKey() => new(null, null, null, null, null);

        /// <summary>A call holding the gate of its key, with whatever that key already answered.</summary>
        public static IdempotentCall Under(
            IAsyncDisposable gate,
            IIdempotencyStore store,
            string scope,
            string key,
            string? remembered) => new(gate, store, scope, key, remembered);

        /// <summary>
        /// Records this call's result under its key, so a repeat of it is answered from the record instead of
        /// doing the work a second time. Called after the work returned.
        /// </summary>
        /// <param name="result">The result the call is about to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public ValueTask RememberAsync(string result, CancellationToken cancellationToken) =>
            _store is null || _scope is null || _key is null
                ? ValueTask.CompletedTask
                : _store.WriteAsync(_scope, _key, result, cancellationToken);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => _gate is null ? ValueTask.CompletedTask : _gate.DisposeAsync();
    }

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
