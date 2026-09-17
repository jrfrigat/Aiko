namespace Aiko.Application.Contracts;

/// <summary>
/// A template as a chooser shows it: identity and provenance, without the documents it carries.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What the template is for.</param>
/// <param name="Version">Content version of the template.</param>
/// <param name="IsDefault">Whether this is the template an init without a choice uses.</param>
/// <param name="IsBuiltIn">
/// Whether this template ships with Aiko rather than living in the templates root. A built-in one has no
/// file to write, so changing it means copying it.
/// </param>
public sealed record ProjectTemplateSummary(
    string Id,
    string Name,
    string Description,
    int Version,
    bool IsDefault,
    bool IsBuiltIn);

/// <summary>
/// Reads the project templates available to this installation.
/// </summary>
/// <remarks>
/// Read-only for now, and deliberately so: the only template that exists is the built-in
/// <see cref="ProjectTemplate.DefaultId"/>, which the store writes on first use. Authoring, importing and
/// exporting templates is the post-MVP half of the feature, and it will extend this port rather than
/// replace it. A project never reads a template at run time - it takes a copy at init and is autonomous
/// afterwards, which is why nothing here takes a project id.
/// </remarks>
public interface IProjectTemplateStore
{
    /// <summary>
    /// Lists the available templates, default first.
    /// </summary>
    ValueTask<IReadOnlyList<ProjectTemplateSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one template in full.
    /// </summary>
    /// <param name="templateId">Template to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">No template with that id exists.</exception>
    ValueTask<ProjectTemplate> ReadAsync(string templateId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the built-in <see cref="ProjectTemplate.DefaultId"/> template when it is missing, and returns
    /// it. Called before an init so a fresh installation has something to create projects from.
    /// </summary>
    ValueTask<ProjectTemplate> EnsureDefaultAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Copies a template into a new one, which is how a shipped base template gets edited: the copy is a
    /// file, so it can be saved, and the base stays as it shipped.
    /// </summary>
    /// <param name="sourceId">Template to copy.</param>
    /// <param name="templateId">Identifier for the copy; it must not exist yet.</param>
    /// <param name="name">Display name for the copy, or null for "&lt;source name&gt; copy".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="FileNotFoundException">The source does not exist.</exception>
    /// <exception cref="IOException">A template with the new id already exists.</exception>
    ValueTask<ProjectTemplate> CopyAsync(
        string sourceId,
        string templateId,
        string? name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes a template over its own file, atomically. The defaults screens save through here, which is
    /// why the write is one document: a template read by an init is either the old one or the new one.
    /// </summary>
    /// <param name="template">The template to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteAsync(ProjectTemplate template, CancellationToken cancellationToken);
}
