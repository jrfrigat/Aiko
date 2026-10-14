namespace Aiko.Application.Contracts;

/// <summary>
/// Applies a template to a project that already exists: the explicit "update this project from the template"
/// action. A project takes a copy of its template at init and is autonomous afterwards, so this is the one
/// deliberate exception - without it, moving an existing project to improved pipelines means editing it by
/// hand, stage by stage - and nothing here is implicit.
/// </summary>
public interface IProjectTemplateApplier
{
    /// <summary>Replaces the project's pipelines, projections and settings with the template's, writes the
    /// template's memory files where the project has none, and adopts the template's git policy.</summary>
    /// <exception cref="InvalidOperationException">The project holds a card in a stage or workflow the
    /// template does not have, which is how a move to a narrower pipeline cannot strand work.</exception>
    /// <exception cref="FileNotFoundException">The project or the template does not exist.</exception>
    ValueTask<ProjectTemplate> ApplyAsync(
        string projectId,
        string templateId,
        CancellationToken cancellationToken);
}
