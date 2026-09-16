namespace Aiko.Application.Agents;

/// <summary>
/// Combined Aiko uninstallation outcome for a project across several selected adapters.
/// </summary>
public sealed record UnifiedUninstallationResult(
    string ProjectId,
    IReadOnlyList<AgentInstallationResult> AdapterResults,
    IReadOnlyList<string> UnknownAdapterIds);
