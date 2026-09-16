namespace Aiko.Server.Contracts;

/// <summary>
/// Request for planning/applying an agent installation: the selected adapter list.
/// </summary>
internal sealed record PlanAgentInstallationRequest(IReadOnlyList<string> SelectedAdapterIds);
