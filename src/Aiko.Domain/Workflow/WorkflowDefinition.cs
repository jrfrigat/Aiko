namespace Aiko.Domain.Workflow;

/// <summary>
/// A project workflow: an ordered set of stages with an optimistic revision.
/// </summary>
public sealed record WorkflowDefinition(
    string Id,
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long Revision);
