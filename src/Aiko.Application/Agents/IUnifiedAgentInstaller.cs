namespace Aiko.Application.Agents;

/// <summary>
/// Facade over a set of <see cref="IAgentAdapter"/> instances: agent discovery,
/// planning and applying install/uninstall of Aiko for several adapters at once.
/// </summary>
public interface IUnifiedAgentInstaller
{
    /// <summary>
    /// Returns every known adapter with its discovered installations for UI display.
    /// </summary>
    ValueTask<IReadOnlyList<AgentAdapterOption>> DiscoverAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a combined install plan for the selected adapters without touching files.
    /// </summary>
    /// <param name="projectId">Identifier of a registered project.</param>
    /// <param name="projectMcpEndpoint">Absolute loopback URL of the project MCP server.</param>
    /// <param name="selectedAdapterIds">Adapter identifiers chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<UnifiedInstallationPlan> PlanAsync(
        string projectId,
        string projectMcpEndpoint,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the Aiko installation for the selected adapters according to the plan.
    /// </summary>
    ValueTask<UnifiedInstallationResult> ApplyAsync(
        string projectId,
        string projectMcpEndpoint,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a combined uninstall plan without touching files.
    /// </summary>
    ValueTask<UnifiedUninstallationPlan> PlanUninstallAsync(
        string projectId,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes Aiko configuration from the project for the selected adapters according to the plan.
    /// </summary>
    ValueTask<UnifiedUninstallationResult> UninstallAsync(
        string projectId,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken);
}
