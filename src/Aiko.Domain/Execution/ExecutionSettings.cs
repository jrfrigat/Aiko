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
        new(WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Deny);

    /// <summary>
    /// Creates settings; <paramref name="maxConcurrentRuns"/> must be at least one.
    /// </summary>
    public ExecutionSettings(WorkspaceMode workspaceMode, int maxConcurrentRuns, ActionPolicy scopeOverlapPolicy, ActionPolicy sharedCheckoutCommitPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRuns, 1);
        WorkspaceMode = workspaceMode;
        MaxConcurrentRuns = maxConcurrentRuns;
        ScopeOverlapPolicy = scopeOverlapPolicy;
        SharedCheckoutCommitPolicy = sharedCheckoutCommitPolicy;
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
}
