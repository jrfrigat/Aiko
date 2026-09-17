using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// One file a template hands to a new project: its path relative to the <c>.aiko</c> directory and
/// the content to write there.
/// </summary>
/// <param name="RelativePath">Path below <c>.aiko</c>, e.g. <c>memory/index.md</c>.</param>
/// <param name="Content">The text to write; only text templates exist, so no encoding choice is offered.</param>
public sealed record TemplateDocument(string RelativePath, string Content);

/// <summary>
/// A project template: everything a new project is created from.
/// </summary>
/// <remarks>
/// Fixed in advance of the feature on purpose. Today exactly one template exists - <see cref="DefaultId"/>,
/// the set Aiko is ready to work with right after installation - but the shape of the thing is settled
/// now, so that "create a project from template X" is a parameter rather than a later migration.
/// A template is also the level the design's "defaults" screens edit: changing a template affects the
/// projects created afterwards, never the ones that already took a copy.
/// </remarks>
/// <param name="Id">Stable identifier, also the directory name below the templates root.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What the template is for, shown where a template is chosen.</param>
/// <param name="Version">Bumped when the template's content changes; recorded on the project it built.</param>
/// <param name="Settings">Default settings, or null to fall back to the global application settings.</param>
/// <param name="Workflows">The workflows the project starts with.</param>
/// <param name="Projections">The board projections the project starts with.</param>
/// <param name="MemoryFiles">The starting memory files.</param>
/// <param name="GitPolicy">
/// The gitignore policy a project created from this template gets. Part of the template for the same reason
/// the settings are: what a project starts as is one decision, not five, and an init that has to be told the
/// policy separately is an init that can disagree with the template it chose.
/// </param>
/// <param name="InitializationInstruction">
/// What an agent must do right after a project has been created from this template, or null when the copy of
/// the template's content is all that is needed.
/// </param>
/// <remarks>
/// The instruction is the template's answer to "a project of this kind is more than files": the module
/// layout to create, the lint config to add, the directories this team expects. It is copied into the new
/// project as <c>.aiko/initialization.md</c> - so the project owns it, exactly as it owns the workflows and
/// the starting memory - and the agent reads it from the project context. A later edit to the template never
/// changes it for a project that already took its copy.
/// </remarks>
public sealed record ProjectTemplate(
    string Id,
    string Name,
    string Description,
    int Version,
    AppSettings? Settings,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<BoardProjectionDefinition> Projections,
    IReadOnlyList<TemplateDocument> MemoryFiles,
    ProjectGitPolicy GitPolicy = ProjectGitPolicy.LocalOnly,
    string? InitializationInstruction = null)
{
    /// <summary>The template every installation has, and the one an init without a choice uses.</summary>
    public const string DefaultId = "default";

    /// <summary>The version the built-in default template is written with.</summary>
    public const int DefaultVersion = 1;
}
