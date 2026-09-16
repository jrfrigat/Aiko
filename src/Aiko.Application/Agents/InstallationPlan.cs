namespace Aiko.Application.Agents;

/// <summary>
/// Install or uninstall plan of Aiko for a single adapter: file changes and warnings.
/// </summary>
public sealed record InstallationPlan(
    string AdapterId,
    IReadOnlyList<InstallationChange> Changes,
    IReadOnlyList<string> Warnings);
