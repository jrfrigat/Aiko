namespace Aiko.Application.Agents;

/// <summary>
/// An agent adapter as a UI choice: capabilities, discovered installations,
/// whether it is selected by default and how much of its user-scope Aiko
/// configuration is already in place.
/// </summary>
public sealed record AgentAdapterOption(
    string Id,
    string DisplayName,
    AgentCapabilities Capabilities,
    IReadOnlyList<AgentInstallation> Installations,
    bool SelectedByDefault,
    AgentUserScope? UserScope = null);
