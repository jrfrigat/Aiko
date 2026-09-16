namespace Aiko.Application.Agents;

/// <summary>
/// Combined Aiko installation plan for a project across several selected adapters.
/// </summary>
public sealed record UnifiedInstallationPlan(
    string ProjectId,
    string ProjectMcpEndpoint,
    IReadOnlyList<InstallationPlan> AdapterPlans,
    IReadOnlyList<string> UnknownAdapterIds);
