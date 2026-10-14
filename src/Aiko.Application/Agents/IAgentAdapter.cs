using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>
/// Adapter of a specific AI agent (Claude Code, Codex, Cursor, ZCode, ...): discovery of its installations, and
/// planning and applying the project-scoped Aiko configuration.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>Stable adapter identifier, for example <c>claude-code</c>.</summary>
    string Id { get; }

    /// <summary>Display name of the agent.</summary>
    string DisplayName { get; }

    /// <summary>Capabilities supported by the agent.</summary>
    AgentCapabilities Capabilities { get; }

    /// <summary>Finds the agent's installations by scanning PATH for its executables.</summary>
    ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(CancellationToken cancellationToken);

    /// <summary>Builds a plan for installing the project-scoped Aiko configuration without touching files.</summary>
    /// <remarks>
    /// <paramref name="projectHandle"/> is needed by an adapter whose configuration lives outside the project, to
    /// name its entry after the project itself - Cline keeps one MCP entry per project in a file shared by every
    /// workspace - rather than after whichever folder the project happens to sit in. <paramref name="cardTypes"/>
    /// comes from the caller because the adapter never reads the project itself, and
    /// <paramref name="accessToken"/> may be null only when the daemon is unprotected.
    /// </remarks>
    ValueTask<InstallationPlan> PlanProjectInstallAsync(
        string projectRoot,
        string projectHandle,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes,
        CancellationToken cancellationToken);

    /// <summary>Applies the project-scoped Aiko configuration idempotently, leaving user settings intact.</summary>
    ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
        string projectRoot,
        string projectHandle,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes,
        CancellationToken cancellationToken);

    /// <summary>Whether Aiko is already connected to this agent in this project: whether the adapter's own files are there.</summary>
    /// <remarks>
    /// Asked before re-projecting the per-type commands after a card type changed: writing agent files into a
    /// project that never installed the agent would be an unasked-for change.
    /// </remarks>
    ValueTask<bool> IsProjectConfiguredAsync(
        string projectRoot,
        CancellationToken cancellationToken);

    /// <summary>Whether the agent is connected at the user scope: any of its files still carries Aiko's entry.</summary>
    /// <remarks>
    /// Asked by a repair before it rewrites the user-scope configuration, for the same reason the project check
    /// is: a connection someone removed must not come back because the agent is still installed.
    /// </remarks>
    ValueTask<bool> IsUserConfiguredAsync(CancellationToken cancellationToken);

    /// <summary>Builds a plan for removing the Aiko configuration from the project without touching files.</summary>
    /// <remarks>
    /// The handle is needed here as much as on install: an adapter that names its entry after the project cannot
    /// find that entry again without it, and would leave it behind.
    /// </remarks>
    ValueTask<InstallationPlan> PlanProjectUninstallAsync(
        string projectRoot,
        string projectHandle,
        CancellationToken cancellationToken);

    /// <summary>Removes only the Aiko-managed content, preserving user settings.</summary>
    ValueTask<AgentInstallationResult> UninstallProjectAsync(
        string projectRoot,
        string projectHandle,
        CancellationToken cancellationToken);

    /// <summary>Builds a plan for installing the user-scoped (global) Aiko configuration without touching files.</summary>
    ValueTask<InstallationPlan> PlanUserInstallAsync(CancellationToken cancellationToken);

    /// <summary>Applies the user-scoped Aiko configuration idempotently, leaving user settings intact.</summary>
    ValueTask<AgentInstallationResult> ApplyUserInstallAsync(CancellationToken cancellationToken);

    /// <summary>Builds a plan for removing the user-scoped Aiko configuration without touching files.</summary>
    ValueTask<InstallationPlan> PlanUserUninstallAsync(CancellationToken cancellationToken);

    /// <summary>Removes only the user-scoped Aiko-managed content.</summary>
    ValueTask<AgentInstallationResult> UninstallUserAsync(CancellationToken cancellationToken);

    /// <summary>Classifies the exit code and stderr of a finished agent process, recognizing a rate limit.</summary>
    ValueTask<AgentAttemptState> ClassifyExitAsync(
        int exitCode,
        string standardError,
        CancellationToken cancellationToken);
}
