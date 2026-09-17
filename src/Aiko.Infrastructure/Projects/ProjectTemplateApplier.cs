using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Applies a template to a project that already exists.
/// </summary>
/// <remarks>
/// The project keeps its cards, its executions and its memory: what a template owns is the pipeline, the
/// projections, the settings and the starting memory, and those are what this replaces. The one thing it
/// refuses is a template that would strand work - a card sitting in a stage the template does not define -
/// because silently dropping that card out of its pipeline is worse than saying no.
/// </remarks>
public sealed class ProjectTemplateApplier(
    IProjectCatalog catalog,
    IProjectDefinitionStore definitions,
    ICardStore cards,
    IAppSettingsStore settings,
    IProjectTemplateStore templates) : IProjectTemplateApplier
{
    /// <inheritdoc />
    public async ValueTask<ProjectTemplate> ApplyAsync(
        string projectId,
        string templateId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var project = await catalog.FindAsync(projectId, cancellationToken)
            ?? throw new FileNotFoundException($"No Aiko project '{projectId}' is registered.");
        var template = await templates.ReadAsync(templateId, cancellationToken);
        var current = await definitions.ReadAsync(projectId, cancellationToken);

        await RefuseStrandedCardsAsync(projectId, current, template, cancellationToken);

        foreach (var workflow in template.Workflows)
        {
            var existing = current.Workflows.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, workflow.Id));
            // The template's stages replace the project's, but the workflow's revision is the project's own:
            // it counts the edits made here, and an applied template is one of them.
            var applied = workflow with { Revision = (existing?.Revision ?? workflow.Revision) + 1 };
            await definitions.SaveWorkflowAsync(
                projectId,
                applied,
                applied.Revision - 1,
                cancellationToken);
        }

        await WriteProjectionsAsync(project.RootPath, template, cancellationToken);
        await WriteMissingMemoryAsync(project.RootPath, template, cancellationToken);
        await ApplySettingsAsync(projectId, template, cancellationToken);
        await ApplyGitPolicyAsync(project, template, cancellationToken);

        return template;
    }

    /// <summary>
    /// Refuses a template that would leave a card outside its pipeline: every card's stage and workflow have
    /// to exist in the template, and a workflow the template leaves out must hold no cards at all - the
    /// template's set replaces the project's, so anything it drops is gone.
    /// </summary>
    private async ValueTask RefuseStrandedCardsAsync(
        string projectId,
        ProjectBoardDefinition current,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        var boardCards = await cards.ListAsync(projectId, cancellationToken);
        foreach (var card in boardCards)
        {
            var workflow = template.Workflows.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, card.WorkflowId));
            if (workflow is null)
            {
                throw new InvalidOperationException(
                    $"Card {card.Reference.CardId} runs workflow {card.WorkflowId}, which template {template.Id} does not define.");
            }

            if (!workflow.Stages.Any(stage => StringComparer.Ordinal.Equals(stage.Id, card.StageId)))
            {
                throw new InvalidOperationException(
                    $"Card {card.Reference.CardId} sits in stage {card.StageId}, which template {template.Id} does not have.");
            }
        }

        foreach (var workflow in current.Workflows)
        {
            if (template.Workflows.Any(candidate => StringComparer.Ordinal.Equals(candidate.Id, workflow.Id)))
            {
                continue;
            }

            var stranded = boardCards.FirstOrDefault(card =>
                StringComparer.Ordinal.Equals(card.WorkflowId, workflow.Id));
            if (stranded is not null)
            {
                throw new InvalidOperationException(
                    $"Card {stranded.Reference.CardId} still runs workflow {workflow.Id}, which template {template.Id} does not define.");
            }
        }
    }

    private static async ValueTask WriteProjectionsAsync(
        string projectRoot,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "projections");
        Directory.CreateDirectory(directory);
        foreach (var projection in template.Projections)
        {
            await WriteJsonAsync(
                Path.Combine(directory, $"{projection.Id}.json"),
                projection,
                AikoJson.Project,
                cancellationToken);
        }
    }

    /// <summary>
    /// Writes the template's memory files where the project has none. A project's memory is what it learned;
    /// a template that overwrote it would erase exactly the knowledge the project exists to keep.
    /// </summary>
    private static async ValueTask WriteMissingMemoryAsync(
        string projectRoot,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        var dataRoot = AikoProjectPaths.DataRoot(projectRoot);
        foreach (var document in template.MemoryFiles)
        {
            var path = Path.Combine(dataRoot, document.RelativePath);
            if (File.Exists(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, document.Content, cancellationToken);
        }
    }

    private async ValueTask ApplySettingsAsync(
        string projectId,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        if (template.Settings is not { } templateSettings)
        {
            return;
        }

        var current = await settings.ReadProjectAsync(projectId, cancellationToken);
        await settings.SaveProjectAsync(
            projectId,
            new AppSettings(
                AppSettings.CurrentSchemaVersion,
                templateSettings.Execution ?? current?.Execution ?? ExecutionSettings.SafeDefault,
                templateSettings.Priority ?? current?.Priority ?? PrioritySettings.SafeDefault),
            cancellationToken);
    }

    /// <summary>
    /// Adopts the template's git policy and records it as the project's origin, the same way an init does:
    /// after this the project is, for provenance purposes, a project of that template.
    /// </summary>
    private static async ValueTask ApplyGitPolicyAsync(
        RegisteredProject project,
        ProjectTemplate template,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "project.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        ProjectManifest? manifest;
        await using (var input = File.OpenRead(manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync(
                input,
                ProjectJsonContext.Default.ProjectManifest,
                cancellationToken);
        }

        if (manifest is null)
        {
            return;
        }

        await WriteJsonAsync(
            manifestPath,
            manifest with
            {
                GitPolicy = template.GitPolicy,
                TemplateId = template.Id,
                TemplateVersion = template.Version
            },
            AikoJson.Project,
            cancellationToken);
    }

    private static async ValueTask WriteJsonAsync<T>(
        string path,
        T document,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(output, document, options, cancellationToken);
    }
}
