using Aiko.Domain.Workflow;

namespace Aiko.Server.Contracts;

/// <summary>
/// Workflow update request: new title, the full stage list
/// and the expected revision for optimistic control.
/// </summary>
internal sealed record UpdateWorkflowRequest(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision);
