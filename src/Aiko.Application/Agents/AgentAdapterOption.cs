namespace Aiko.Application.Agents;

/// <summary>
/// An agent adapter as a UI choice: capabilities, discovered installations
/// and whether it is selected by default.
/// </summary>
public sealed record AgentAdapterOption(
    string Id,
    string DisplayName,
    AgentCapabilities Capabilities,
    IReadOnlyList<AgentInstallation> Installations,
    bool SelectedByDefault);
