using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Server.Contracts;
using Aiko.Server.Workflow;

namespace Aiko.Server.Mcp;

/// <summary>
/// Daemon-level MCP tools that do not require a project: listing and initializing projects
/// and creating cards in a project (including cross-project cards).
/// These back the global <c>/aiko-init</c> and <c>/aiko-list-projects</c> skills.
/// </summary>
[McpServerToolType]
internal sealed class DaemonTools(
    IProjectCatalog catalog,
    IProjectInitializer initializer,
    IProjectTemplateStore templates,
    ICardStore cards,
    IProjectDefinitionStore definitions)
{
    [McpServerTool(Name = "aiko_list_projects", Title = "List Aiko projects")]
    [Description("Lists all projects registered with the Aiko daemon.")]
    public async Task<string> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await catalog.ListAsync(cancellationToken);
        return JsonSerializer.Serialize(projects, ServerJsonContext.Default.IReadOnlyListRegisteredProject);
    }

    [McpServerTool(Name = "aiko_list_templates", Title = "List Aiko project templates")]
    [Description(
        "Lists the project templates a new project can be created from. Ask the user which one to use "
        + "before calling aiko_init_project when more than one is listed.")]
    public async Task<string> ListTemplatesAsync(CancellationToken cancellationToken)
    {
        var listed = await templates.ListAsync(cancellationToken);
        return JsonSerializer.Serialize(listed, ServerJsonContext.Default.IReadOnlyListProjectTemplateSummary);
    }

    [McpServerTool(Name = "aiko_init_project", Title = "Initialize Aiko project")]
    [Description(
        "Registers a local directory as an Aiko project by copying the chosen template into it: the .aiko "
        + "structure, the workflows with their stages and artifacts, the board projections, the starting "
        + "memory and the default settings. A later change to a template does not reach a project created "
        + "before it.")]
    public async Task<string> InitProjectAsync(
        [Description("Absolute path to the project root directory.")]
        string rootPath,
        [Description("Optional project name. Defaults to the directory name.")]
        [Optional] string? name,
        [Description(
            "Optional project id: the short handle used in the UI's URLs, for example 'aiko'. Defaults to a "
            + "slug derived from the name. A value that is already taken is refused; a derived one is made "
            + "unique automatically.")]
        [Optional] string? projectId,
        [Description("Git policy: local-only, track-project-knowledge or custom. Defaults to local-only.")]
        [Optional] string? gitPolicy,
        [Description("Template id from aiko_list_templates. Defaults to the built-in default template.")]
        [Optional] string? templateId,
        CancellationToken cancellationToken)
    {
        var project = await initializer.InitializeAsync(
            new InitializeProjectRequest(rootPath, name, ParseGitPolicy(gitPolicy), templateId, projectId),
            cancellationToken);
        return JsonSerializer.Serialize(project, ServerJsonContext.Default.RegisteredProject);
    }

    [McpServerTool(Name = "aiko_create_card_in_project", Title = "Create Aiko card in a project")]
    [Description(
        "Creates a card of any type the project defines in the given project, validating the stage against that project's workflow. Pass originProjectId when reporting from another project.")]
    public async Task<string> CreateCardAsync(
        [Description("Target project id.")]
        string projectId,
        [Description("File-safe card id, for example TASK-001.")]
        string cardId,
        [Description("Card type: story, task, or any type the project added in its workflow editor.")]
        string kind,
        [Description("Human-readable title.")]
        string title,
        [Description("Workflow id that defines the type, for example story or task.")]
        string workflowId,
        [Description("Initial stage id.")]
        string stageId,
        [Description("Own priority score, zero or greater.")]
        decimal ownPriority,
        [Description("Initial declared scope file patterns.")]
        string[] declaredScopeFiles,
        [Description("Source project id when reporting from another project.")]
        [Optional] string? originProjectId,
        [Description("Source card id when reporting from another project.")]
        [Optional] string? originCardId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        var reference = new CardReference(projectId, cardId);
        if (await cards.FindAsync(reference, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Card '{cardId}' already exists in project {projectId}.");
        }

        var card = new Card(
            reference,
            ParseKind(kind),
            title,
            workflowId,
            stageId,
            1,
            ownPriority,
            declaredScopeFiles,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            originProjectId is null
                ? null
                : new CardOrigin(originProjectId, originCardId, null, DateTimeOffset.UtcNow));

        var stage = await CardStageValidation.FindValidStageAsync(card, stageId, definitions, cancellationToken)
            ?? throw new ArgumentException(
                $"Stage '{stageId}' is not valid for the card workflow in project {projectId}.",
                nameof(stageId));

        await cards.SaveAsync(card, 0, cancellationToken);
        return JsonSerializer.Serialize(card, ServerJsonContext.Default.Card);
    }

    private static string ParseKind(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Card kind is required.", nameof(value))
            : CardKind.Canonical(value);

    private static ProjectGitPolicy ParseGitPolicy(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ProjectGitPolicy.LocalOnly
            : value.Trim().ToLowerInvariant() switch
            {
                "local-only" => ProjectGitPolicy.LocalOnly,
                "track-project-knowledge" => ProjectGitPolicy.TrackProjectKnowledge,
                "custom" => ProjectGitPolicy.Custom,
                _ => throw new ArgumentException(
                    "Git policy must be local-only, track-project-knowledge or custom.",
                    nameof(value))
            };
}