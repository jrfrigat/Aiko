namespace Aiko.Application.Agents;

/// <summary>
/// Combined Aiko uninstallation plan for a project across several selected adapters.
/// </summary>
public sealed record UnifiedUninstallationPlan(
    string ProjectId,
    IReadOnlyList<InstallationPlan> AdapterPlans,
    IReadOnlyList<string> UnknownAdapterIds);
