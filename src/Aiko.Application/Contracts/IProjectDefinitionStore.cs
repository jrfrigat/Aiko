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

    /// <summary>
    /// Creates a workflow the project does not have yet, which is how a user adds a card type of their own.
    /// </summary>
    /// <param name="projectId">Project to write into.</param>
    /// <param name="workflow">The workflow to create; its revision must be 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The project already has a workflow with that id.</exception>
    ValueTask CreateWorkflowAsync(
        string projectId,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a workflow, and with it the card type it defined.
    /// </summary>
    /// <param name="projectId">Project to write into.</param>
    /// <param name="workflowId">Workflow to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">The project has no such workflow.</exception>
    ValueTask DeleteWorkflowAsync(
        string projectId,
        string workflowId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives a workflow a new id - renaming the card type it defines. Its pipeline, its board projections
    /// and the type's place in the agent vocabulary all follow the new id.
    /// </summary>
    /// <remarks>
    /// The id is the type's identity rather than a label: it names the folder a type's cards are filed in
    /// and the board section that shows them. A type that already has cards is not renamed here - the
    /// caller refuses that before the files are touched, because moving them is a different operation.
    /// </remarks>
    /// <param name="projectId">Project to write into.</param>
    /// <param name="currentId">Workflow to rename.</param>
    /// <param name="newId">The id it should carry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The renamed workflow.</returns>
    /// <exception cref="KeyNotFoundException">The project has no such workflow.</exception>
    /// <exception cref="InvalidOperationException">A workflow already has the requested id.</exception>
    ValueTask<WorkflowDefinition> RenameWorkflowAsync(
        string projectId,
        string currentId,
        string newId,
        CancellationToken cancellationToken);
}
