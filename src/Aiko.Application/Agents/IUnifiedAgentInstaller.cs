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
    /// Applies the user-scoped Aiko configuration of a single adapter - the global <c>/aiko-*</c> skills
    /// and commands a machine-wide connection consists of.
    /// </summary>
    /// <returns>The outcome, or null when the identifier is unknown to this installer.</returns>
    ValueTask<AgentInstallationResult?> ApplyUserInstallAsync(
        string adapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports, for every known adapter, whether it is connected to the given project.
    /// </summary>
    /// <remarks>
    /// Derived from the files an install owns, so the answer cannot disagree with what is on disk. This is the
    /// project-scoped half of the agent list: an agent connected machine-wide still has to be connected to a
    /// project before that project has anything of its own for the agent to read.
    /// </remarks>
    /// <param name="projectId">Identifier of a registered project, by id or by handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<IReadOnlyList<AgentProjectConnection>> ReadProjectConnectionsAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the user-scoped Aiko configuration of a single adapter, leaving user files alone.
    /// </summary>
    /// <returns>The outcome, or null when the identifier is unknown to this installer.</returns>
    ValueTask<AgentInstallationResult?> UninstallUserAsync(
        string adapterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a combined install plan for the selected adapters without touching files.
    /// </summary>
    /// <param name="projectId">Identifier of a registered project.</param>
    /// <param name="projectMcpEndpoint">Absolute loopback URL of the project MCP server.</param>
    /// <param name="accessToken">
    /// The daemon's access token, written into every generated MCP entry: the endpoint requires it, and
    /// without it the agent gets 401.
    /// </param>
    /// <param name="selectedAdapterIds">Adapter identifiers chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<UnifiedInstallationPlan> PlanAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the Aiko installation for the selected adapters according to the plan.
    /// </summary>
    ValueTask<UnifiedInstallationResult> ApplyAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
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

    /// <summary>
    /// Re-applies the project files that are derived from the project's card types - the per-type
    /// <c>/aiko-create-&lt;type&gt;</c> commands - and removes the ones whose type no longer exists.
    /// </summary>
    /// <remarks>
    /// Called when a workflow is created, renamed or removed: the set of card types is project data, while
    /// the agent files are a projection of it, so the projection is refreshed where the data changes. Only
    /// adapters already connected to the project are touched, so this never writes agent files into a project
    /// that did not ask for them.
    /// </remarks>
    /// <param name="projectId">Identifier of a registered project.</param>
    /// <param name="projectMcpEndpoint">Absolute loopback URL of the project MCP server.</param>
    /// <param name="accessToken">The daemon's access token, as in <see cref="ApplyAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>One result per adapter that was re-projected; empty when none is connected.</returns>
    ValueTask<IReadOnlyList<AgentInstallationResult>> ReprojectCardTypesAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
        CancellationToken cancellationToken);
}
