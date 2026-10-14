using Aiko.Domain.Workflow;

namespace Aiko.Application.Contracts;

/// <summary>
/// One file a template hands to a new project: its path relative to the <c>.aiko</c> directory and the text
/// to write there. Only text templates exist, so no encoding choice is offered.
/// </summary>
public sealed record TemplateDocument(string RelativePath, string Content);

/// <summary>
/// A project template: everything a new project is created from, and what the defaults screens edit -
/// changing one affects projects created afterwards, never the ones that took a copy.
/// </summary>
/// <remarks>
/// <c>InitializationInstruction</c> is the template's answer to "a project of this kind is more than files" -
/// the module layout to create, the lint config to add. It is copied into the new project as
/// <c>.aiko/initialization.md</c>, so the project owns it, and a later edit to the template never changes it
/// for a project that took its copy.
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

    /// <summary>
    /// The version the built-in default template is written with. Bumped whenever its content changes, so a
    /// project's manifest says which revision of the defaults it was created from.
    /// </summary>
    public const int DefaultVersion = 6;
}
