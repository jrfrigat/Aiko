namespace Aiko.Application.Agents;

/// <summary>
/// A planned change to a single file while installing or removing Aiko.
/// </summary>
public sealed record InstallationChange(string Path, string Description);
