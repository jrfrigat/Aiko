namespace Aiko.Application.Agents;

/// <summary>
/// Combined Aiko installation outcome for a project across several selected adapters.
/// </summary>
public sealed record UnifiedInstallationResult(
    string ProjectId,
    string ProjectMcpEndpoint,
    IReadOnlyList<AgentInstallationResult> AdapterResults,
    IReadOnlyList<string> UnknownAdapterIds);
