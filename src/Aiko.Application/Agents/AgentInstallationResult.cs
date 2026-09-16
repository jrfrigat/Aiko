namespace Aiko.Application.Agents;

/// <summary>
/// Install or uninstall outcome of Aiko for a single adapter.
/// </summary>
public sealed record AgentInstallationResult(
    string AdapterId,
    bool Succeeded,
    IReadOnlyList<InstallationFileResult> Files,
    IReadOnlyList<string> Warnings);
