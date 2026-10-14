using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// Store of project definitions: workflows and board projections inside .aiko.
/// </summary>
public interface IProjectDefinitionStore
{
    /// <summary>Reads all workflows and projections of the project.</summary>
    ValueTask<ProjectBoardDefinition> ReadAsync(
        string projectId,
        CancellationToken cancellationToken);

    /// <summary>Saves a workflow with an optimistic revision check: the caller passes the revision it read,
    /// and the stored one becomes that plus one.</summary>
    ValueTask SaveWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        long expectedRevision,
        CancellationToken cancellationToken);

    /// <summary>Creates a workflow the project does not have yet, which is how a user adds a card type of
    /// their own; the workflow's revision must be 1.</summary>
    /// <exception cref="InvalidOperationException">The project already has a workflow with that id.</exception>
    ValueTask CreateWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken);

    /// <summary>Removes a workflow, and with it the card type it defined.</summary>
    /// <exception cref="KeyNotFoundException">The project has no such workflow.</exception>
    ValueTask DeleteWorkflowAsync(
        string projectId,
        string workflowId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives a workflow a new id - renaming the card type it defines; its pipeline, its board projections and
    /// the type's place in the agent vocabulary all follow. The id is the type's identity rather than a label:
    /// it names the folder a type's cards are filed in and the board section that shows them, so a type that
    /// already has cards is not renamed here - the caller refuses that before the files are touched, because
    /// moving them is a different operation.
    /// </summary>
    /// <returns>The renamed workflow.</returns>
    /// <exception cref="KeyNotFoundException">The project has no such workflow.</exception>
    /// <exception cref="InvalidOperationException">A workflow already has the requested id.</exception>
    ValueTask<WorkflowDefinition> RenameWorkflowAsync(
        string projectId,
        string currentId,
        string newId,
        CancellationToken cancellationToken);
}
