using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>
/// Adapter of a specific AI agent (Claude Code, Codex, Cursor, ZCode, etc.):
/// installation discovery, planning and applying the project-scoped Aiko configuration.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>
    /// Stable adapter identifier, for example <c>claude-code</c>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the agent.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Capabilities supported by the agent.
    /// </summary>
    AgentCapabilities Capabilities { get; }

    /// <summary>
    /// Finds all agent installations by scanning PATH for the executables.
    /// </summary>
    ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Builds a plan for installing the project-scoped Aiko configuration without touching files.
    /// </summary>
    /// <param name="projectRoot">Full path to the project root.</param>
    /// <param name="projectMcpEndpoint">Absolute loopback URL of the project MCP server.</param>
    /// <param name="accessToken">
    /// The daemon's access token. The MCP endpoint requires it, so it belongs in the configuration the
    /// adapter writes; null only when there is no token (an unprotected daemon).
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    ValueTask<InstallationPlan> PlanProjectInstallAsync(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the project-scoped Aiko configuration idempotently,
    /// leaving user settings intact.
    /// </summary>
    ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a plan for removing the Aiko configuration from the project without touching files.
    /// </summary>
    ValueTask<InstallationPlan> PlanProjectUninstallAsync(
        string projectRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes only the Aiko-managed content, preserving user settings.
    /// </summary>
    ValueTask<AgentInstallationResult> UninstallProjectAsync(
        string projectRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds a plan for installing the user-scoped (global) Aiko configuration without touching files.
    /// </summary>
    ValueTask<InstallationPlan> PlanUserInstallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies the user-scoped Aiko configuration idempotently, leaving user settings intact.
    /// </summary>
    ValueTask<AgentInstallationResult> ApplyUserInstallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Builds a plan for removing the user-scoped Aiko configuration without touching files.
    /// </summary>
    ValueTask<InstallationPlan> PlanUserUninstallAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes only the user-scoped Aiko-managed content.
    /// </summary>
    ValueTask<AgentInstallationResult> UninstallUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Classifies the exit code and stderr of a finished agent process,
    /// in particular recognizing rate-limit exhaustion.
    /// </summary>
    ValueTask<AgentAttemptState> ClassifyExitAsync(
        int exitCode,
        string standardError,
        CancellationToken cancellationToken);
}
