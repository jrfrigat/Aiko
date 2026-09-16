using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Store of project definitions: workflows and board projections inside .aiko.
/// </summary>
public interface IProjectDefinitionStore
{
    /// <summary>
    /// Reads all workflows and projections of the project.
    /// </summary>
    ValueTask<ProjectBoardDefinition> ReadAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a workflow with an optimistic revision check
    /// (<paramref name="expectedRevision"/> + 1).
    /// </summary>
    ValueTask SaveWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        long expectedRevision,
        CancellationToken cancellationToken);
}
