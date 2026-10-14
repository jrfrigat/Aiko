namespace Aiko.Application.Contracts;

/// <summary>
/// A template as a chooser shows it: identity and provenance, without the documents it carries. A built-in
/// one ships with Aiko rather than living in the templates root, so it has no file to write and changing it
/// means copying it.
/// </summary>
public sealed record ProjectTemplateSummary(
    string Id,
    string Name,
    string Description,
    int Version,
    bool IsDefault,
    bool IsBuiltIn);

/// <summary>
/// Reads the project templates available to this installation. Read-only for now - the only template that
/// exists is the built-in <see cref="ProjectTemplate.DefaultId"/> - and a project never reads a template at
/// run time: it takes a copy at init and is autonomous afterwards, which is why nothing here takes a project
/// id.
/// </summary>
public interface IProjectTemplateStore
{
    /// <summary>Lists the available templates, default first.</summary>
    ValueTask<IReadOnlyList<ProjectTemplateSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Reads one template in full.</summary>
    /// <exception cref="FileNotFoundException">No template with that id exists.</exception>
    ValueTask<ProjectTemplate> ReadAsync(string templateId, CancellationToken cancellationToken);

    /// <summary>Writes the built-in <see cref="ProjectTemplate.DefaultId"/> template when it is missing, so a
    /// fresh installation has something to create projects from.</summary>
    ValueTask<ProjectTemplate> EnsureDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Copies a template into a new one, which is how a shipped base template gets edited: the copy
    /// is a file, so it can be saved, and the base stays as it shipped. The copy is named "&lt;source name&gt;
    /// copy" unless a name is given.</summary>
    /// <exception cref="FileNotFoundException">The source does not exist.</exception>
    /// <exception cref="IOException">A template with the new id already exists.</exception>
    ValueTask<ProjectTemplate> CopyAsync(
        string sourceId,
        string templateId,
        string? name,
        CancellationToken cancellationToken);

    /// <summary>Writes a template over its own file, atomically: a template read by an init is either the old
    /// one or the new one.</summary>
    ValueTask WriteAsync(ProjectTemplate template, CancellationToken cancellationToken);

    /// <summary>Builds a template out of a project: its settings snapshot, pipelines, projections, memory and
    /// git policy. The template is named "&lt;project name&gt; template" unless a name is given.</summary>
    /// <exception cref="DirectoryNotFoundException">The project has no <c>.aiko</c> directory.</exception>
    /// <exception cref="IOException">A template with the new id already exists.</exception>
    ValueTask<ProjectTemplate> CreateFromProjectAsync(
        string projectRootPath,
        string templateId,
        string? name,
        CancellationToken cancellationToken);

    /// <summary>Writes a template's document to a path outside the templates root: the same JSON the store
    /// writes, so an export is a template by another name. An existing file is overwritten.</summary>
    ValueTask ExportAsync(string templateId, string path, CancellationToken cancellationToken);

    /// <summary>Reads a template document from outside the templates root and stores it under its own id, or
    /// under the id given - a different id is how the same file is imported twice.</summary>
    /// <exception cref="InvalidDataException">The file is not a template document.</exception>
    /// <exception cref="IOException">A template with the resulting id already exists.</exception>
    ValueTask<ProjectTemplate> ImportAsync(
        string path,
        string? templateId,
        CancellationToken cancellationToken);

    /// <summary>Removes a template that lives in the templates root. The shipped base is not a file, so it
    /// cannot be deleted; a copy of it can.</summary>
    /// <returns>True when a stored template was removed; false when there was none.</returns>
    ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken);
}
