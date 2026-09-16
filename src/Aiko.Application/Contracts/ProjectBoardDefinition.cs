using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Project definitions loaded from files: workflows and board projections.
/// </summary>
public sealed record ProjectBoardDefinition(
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<BoardProjectionDefinition> Projections);
