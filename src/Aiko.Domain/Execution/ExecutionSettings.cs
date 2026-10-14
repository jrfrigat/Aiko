using Aiko.Domain.Workflow;

namespace Aiko.Domain.Execution;

/// <summary>
/// Execution settings: workspace mode, concurrency and action policies.
/// </summary>
public sealed record ExecutionSettings
{
    /// <summary>
    /// Conservative: one run at a time in the shared checkout, and nothing leaves it without a person
    /// saying so.
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
    /// Whether the shared checkout may be pushed from. Optional: a document written before the field existed
    /// carries none, and a missing value has to read as the safe one rather than failing.
    /// </param>
    /// <param name="scopeExpansionPolicy">
    /// What to do when an agent needs a file outside the card's declared scope. Optional like the push policy,
    /// and a document written before it reads as <see cref="ActionPolicy.Ask"/> - the behaviour Aiko had - not
    /// as permission.
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

    public WorkspaceMode WorkspaceMode { get; }

    public int MaxConcurrentRuns { get; }

    public ActionPolicy ScopeOverlapPolicy { get; }

    public ActionPolicy SharedCheckoutCommitPolicy { get; }

    /// <summary>
    /// Policy for pushing from the shared project checkout.
    /// </summary>
    /// <remarks>
    /// A rule an agent reads rather than a gate it waits on: Aiko has no push of its own, so nothing can pause
    /// an execution until the user approves. Stated beside the commit policy so both questions are answered in
    /// one place.
    /// </remarks>
    public ActionPolicy SharedCheckoutPushPolicy { get; }

    /// <summary>
    /// Policy for widening a card's declared scope while a stage runs.
    /// </summary>
    /// <remarks>
    /// Enforced rather than merely stated: the coordinator reads it when an agent asks for a file outside the
    /// declared scope. <see cref="ActionPolicy.Ask"/> moves the run to waiting-for-user,
    /// <see cref="ActionPolicy.Allow"/> adds the files and lets it continue, and <see cref="ActionPolicy.Deny"/>
    /// refuses without touching anything.
    /// </remarks>
    public ActionPolicy ScopeExpansionPolicy { get; }
}
