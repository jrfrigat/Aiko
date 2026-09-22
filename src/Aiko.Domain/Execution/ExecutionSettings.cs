using Aiko.Domain.Workflow;

namespace Aiko.Domain.Execution;

/// <summary>
/// Execution settings: workspace mode, concurrency and action policies.
/// </summary>
public sealed record ExecutionSettings
{
    /// <summary>
    /// Conservative defaults: shared workspace, a single run, ask on scope overlap
    /// and deny commits into the shared checkout.
    /// </summary>
    public static ExecutionSettings SafeDefault { get; } =
        new(WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Deny, ActionPolicy.Deny, ActionPolicy.Ask);

    /// <summary>
    /// Creates settings; <paramref name="maxConcurrentRuns"/> must be at least one.
    /// </summary>
    /// <param name="workspaceMode">Which checkout the runs work in.</param>
    /// <param name="maxConcurrentRuns">How many stage executions may be active at once.</param>
    /// <param name="scopeOverlapPolicy">What to do when parallel runs declare overlapping scope.</param>
    /// <param name="sharedCheckoutCommitPolicy">Whether the shared checkout may be committed.</param>
    /// <param name="sharedCheckoutPushPolicy">
    /// Whether the shared checkout may be pushed from. Optional on purpose: settings documents written before it
    /// existed carry no such field, and a missing value has to read as the safe one rather than failing.
    /// </param>
    /// <param name="scopeExpansionPolicy">
    /// What to do when an agent needs a file outside the card's declared scope. Optional on purpose, like the
    /// push policy above: a document written before it exists reads as <see cref="ActionPolicy.Ask"/> - the
    /// behaviour Aiko had when asking was the only thing it could do - rather than as permission.
    /// </param>
    public ExecutionSettings(
        WorkspaceMode workspaceMode,
        int maxConcurrentRuns,
        ActionPolicy scopeOverlapPolicy,
        ActionPolicy sharedCheckoutCommitPolicy,
        ActionPolicy sharedCheckoutPushPolicy = ActionPolicy.Deny,
        ActionPolicy scopeExpansionPolicy = ActionPolicy.Ask)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRuns, 1);
        WorkspaceMode = workspaceMode;
        MaxConcurrentRuns = maxConcurrentRuns;
        ScopeOverlapPolicy = scopeOverlapPolicy;
        SharedCheckoutCommitPolicy = sharedCheckoutCommitPolicy;
        SharedCheckoutPushPolicy = sharedCheckoutPushPolicy;
        ScopeExpansionPolicy = scopeExpansionPolicy;
    }

    /// <summary>
    /// Workspace mode for runs.
    /// </summary>
    public WorkspaceMode WorkspaceMode { get; }

    /// <summary>
    /// Maximum number of concurrent executions.
    /// </summary>
    public int MaxConcurrentRuns { get; }

    /// <summary>
    /// Policy for overlapping declared scope of parallel executions.
    /// </summary>
    public ActionPolicy ScopeOverlapPolicy { get; }

    /// <summary>
    /// Policy for committing into the shared project checkout.
    /// </summary>
    public ActionPolicy SharedCheckoutCommitPolicy { get; }

    /// <summary>
    /// Policy for pushing from the shared project checkout.
    /// </summary>
    /// <remarks>
    /// A rule an agent reads rather than a gate it waits on: Aiko has no push of its own - <c>IGitClient</c>
    /// reads status, log and diff and nothing else - so nothing can pause an execution until the user approves.
    /// It is stated the same way as the commit policy so a project answers both questions in one place; the
    /// enforcement arrives with the push action itself.
    /// </remarks>
    public ActionPolicy SharedCheckoutPushPolicy { get; }

    /// <summary>
    /// Policy for widening a card's declared scope while a stage runs.
    /// </summary>
    /// <remarks>
    /// Unlike the push policy above, this one is enforced rather than merely stated: the coordinator reads it
    /// when an agent asks for a file outside the declared scope. <see cref="ActionPolicy.Ask"/> moves the run
    /// to the waiting-for-user state (what Aiko did before the policy existed), <see cref="ActionPolicy.Allow"/>
    /// adds the files to the card's declared scope and lets the run keep going, and <see cref="ActionPolicy.Deny"/>
    /// refuses the request outright without touching anything.
    /// </remarks>
    public ActionPolicy ScopeExpansionPolicy { get; }
}
