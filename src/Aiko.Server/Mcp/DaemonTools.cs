using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Cards;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
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
    IProjectDefinitionStore definitions,
    IProjectLinkStore links,
    IAppSettingsService settings,
    IAikoEventPublisher events)
{
    [McpServerTool(Name = "aiko_list_projects", Title = "List Aiko projects")]
    [Description("Lists all projects registered with the Aiko daemon.")]
    public async Task<string> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await catalog.ListAsync(cancellationToken);
        return JsonSerializer.Serialize(projects, ServerJsonContext.Default.IReadOnlyListRegisteredProject);
    }

    [McpServerTool(Name = "aiko_link_project", Title = "Link an Aiko project")]
    [Description(
        "Links a project to another one and records what the linked project is for. That description is what an "
        + "agent reads when it decides whether a piece of work belongs to the neighbour, so it is required and "
        + "written in the words of whoever links. Both projects are addressed by id or by the readable handle; "
        + "linking a project to itself is refused, and so is linking a project nobody has registered.")]
    public async Task<string> LinkProjectAsync(
        [Description("Project whose registry is written, by id or by its readable handle.")]
        string projectId,
        [Description("Project to link, by id or by its readable handle.")]
        string targetProjectId,
        [Description("What the linked project is for, in the words of whoever links it.")]
        string description,
        CancellationToken cancellationToken)
    {
        var link = await links.SaveAsync(projectId, targetProjectId, description, cancellationToken);
        return JsonSerializer.Serialize(link, ServerJsonContext.Default.ProjectLink);
    }

    [McpServerTool(Name = "aiko_unlink_project", Title = "Unlink an Aiko project")]
    [Description(
        "Removes the link to a project and returns what is left. Cards already filed there stay where they are: "
        + "a link routes future work, it does not keep the work that was already routed alive.")]
    public async Task<string> UnlinkProjectAsync(
        [Description("Project whose registry is written, by id or by its readable handle.")]
        string projectId,
        [Description("Linked project to remove, by id or by its readable handle.")]
        string targetProjectId,
        CancellationToken cancellationToken)
    {
        await links.RemoveAsync(projectId, targetProjectId, cancellationToken);
        var remaining = await links.ListAsync(projectId, cancellationToken);
        return JsonSerializer.Serialize(remaining, ServerJsonContext.Default.IReadOnlyListProjectLink);
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
        + "before it. The template may also carry an initialization instruction (for example the structure "
        + "the project should have): after creating the project, read aiko_get_project_context and carry "
        + "that instruction out before starting work.")]
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
        "Creates a card of any type the project defines in the given project. The card lands in that "
        + "project's workflow backlog stage; Aiko names it, so pass no id unless you are importing a card "
        + "that already has one. Pass originProjectId when reporting from another project - the project you "
        + "are working in then decides whether the write is allowed: 'deny' refuses, 'ask' refuses until you "
        + "have asked the user and repeat the call with userConfirmed=true, and 'allow' creates it.")]
    public async Task<string> CreateCardAsync(
        [Description("Target project id.")]
        string projectId,
        [Description("Card type: story, task, or any type the project added in its workflow editor.")]
        string kind,
        [Description("Human-readable title.")]
        string title,
        [Description("Own priority score, zero or greater.")]
        decimal ownPriority,
        [Description("File-safe card id, only when importing a card that already has one.")]
        [Optional] string? cardId,
        [Description("Workflow id that defines the type. Defaults to the kind's own id.")]
        [Optional] string? workflowId,
        [Description("Declared scope file patterns.")]
        [Optional] string[]? declaredScopeFiles,
        [Description("What the card is asked to do, when the title alone is not enough.")]
        [Optional] string? requirements,
        [Description("Source project id when reporting from another project.")]
        [Optional] string? originProjectId,
        [Description("Source card id when reporting from another project.")]
        [Optional] string? originCardId,
        [Description(
            "Set true only after the user agreed to the hand-over, when the source project's policy is 'ask'.")]
        bool userConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownPriority);
        // The target may be named by its readable handle; the card is filed under the project's own id.
        var project = await catalog.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");

        // A cross-project call writes into someone else's project, so the project the agent works in decides:
        // it may refuse, ask the user first, or allow it. The target grants nothing and configures nothing -
        // it only sees the origin mark and decides what to do with the card.
        string? sourceProjectId = null;
        if (!string.IsNullOrWhiteSpace(originProjectId))
        {
            var source = await catalog.FindAsync(originProjectId, cancellationToken)
                ?? throw new KeyNotFoundException($"Unknown Aiko project: {originProjectId}");
            sourceProjectId = source.Id;
            var policy = await settings.GetEffectiveCrossProjectAsync(source.Id, cancellationToken);
            if (policy.Targets.Count > 0 &&
                !policy.Targets.Contains(project.Id, StringComparer.OrdinalIgnoreCase) &&
                !policy.Targets.Contains(project.Handle, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Project {source.Handle} does not allow writing to {project.Handle}: the target is not in "
                    + "its cross-project list.");
            }

            switch (policy.WritePolicy)
            {
                case CrossProjectWritePolicy.Deny:
                    throw new InvalidOperationException(
                        $"Cross-project writing is denied by project {source.Handle}: create the card manually "
                        + $"in {project.Handle} instead.");
                case CrossProjectWritePolicy.Ask when !userConfirmed:
                    throw new InvalidOperationException(
                        $"Project {source.Handle} asks before writing to {project.Handle}: ask the user, then "
                        + "call again with userConfirmed=true.");
            }
        }

        var canonicalKind = ParseKind(kind);
        var (workflow, backlog, reason) = await CardCreation.ResolveAsync(
            definitions,
            project.Id,
            canonicalKind,
            workflowId,
            cancellationToken);
        if (workflow is null || backlog is null)
        {
            throw new ArgumentException(reason!, nameof(kind));
        }

        var resolvedId = string.IsNullOrWhiteSpace(cardId)
            ? await CardIdGenerator.NextAsync(cards, project.Id, canonicalKind, cancellationToken)
            : cardId.Trim();
        var reference = new CardReference(project.Id, resolvedId);
        if (await cards.FindAsync(reference, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Card '{resolvedId}' already exists in project {projectId}.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(requirements))
        {
            metadata[Card.RequirementsMetadataKey] = requirements.Trim();
        }

        var card = new Card(
            reference,
            canonicalKind,
            title,
            workflow.Id,
            backlog.Id,
            1,
            ownPriority,
            declaredScopeFiles ?? [],
            [],
            metadata,
            originProjectId is null
                ? null
                : new CardOrigin(originProjectId, originCardId, null, DateTimeOffset.UtcNow));

        await cards.SaveAsync(card, 0, cancellationToken);
        if (sourceProjectId is not null)
        {
            // Both journals see the hand-over: the target's card.updated is written by the card store, and the
            // source keeps its own record of what left the project.
            await events.PublishAsync(
                sourceProjectId,
                AikoEventTypes.CrossProjectCardCreated,
                JsonSerializer.Serialize(
                    new CrossProjectCardEvent(card.Reference.CardId, project.Id),
                    ServerJsonContext.Default.CrossProjectCardEvent),
                cancellationToken);
        }

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