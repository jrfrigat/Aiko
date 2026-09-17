namespace Aiko.Application.Contracts;

/// <summary>
/// Applies a template to a project that already exists: the explicit "update this project from the
/// template" action.
/// </summary>
/// <remarks>
/// A project takes a copy of its template at init and is autonomous afterwards - that is the whole point of
/// the feature. This port is the one deliberate exception, and it exists because the alternative is worse:
/// without it, moving an existing project to an improved set of pipelines means editing it by hand, stage by
/// stage. Nothing here is implicit; the caller asked for it by name.
/// </remarks>
public interface IProjectTemplateApplier
{
    /// <summary>
    /// Replaces the project's pipelines, projections and settings with the template's, writes the template's
    /// memory files where the project has none, and adopts the template's git policy.
    /// </summary>
    /// <param name="projectId">Project to update.</param>
    /// <param name="templateId">Template to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The applied template.</returns>
    /// <exception cref="InvalidOperationException">
    /// The project holds a card in a stage the template does not have, or has a card in a workflow the
    /// template does not define. Refusing is how a move to a narrower pipeline cannot strand work.
    /// </exception>
    /// <exception cref="FileNotFoundException">The project or the template does not exist.</exception>
    ValueTask<ProjectTemplate> ApplyAsync(
        string projectId,
        string templateId,
        CancellationToken cancellationToken);
}
