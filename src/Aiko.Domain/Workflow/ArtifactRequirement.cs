namespace Aiko.Domain.Workflow;

/// <summary>
/// Required Markdown artifact of a stage, for example <c>analysis.md</c>.
/// </summary>
public sealed record ArtifactRequirement(
    string Path,
    string Description,
    MissingArtifactPolicy MissingPolicy);
