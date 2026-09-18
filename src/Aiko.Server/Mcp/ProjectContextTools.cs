using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Storage;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools describing the current project: the card types it defines, context for planning work and the
/// UI URL.
/// </summary>
[McpServerToolType]
internal sealed class ProjectContextTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IProjectDefinitionStore definitions,
    IAppSettingsService settings) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(
        Name = "aiko_get_project_context",
        Title = "Get Aiko project context")]
    [Description(
        "Call this first. Returns the current project, every card type it defines with the stages of its "
        + "pipeline, how its cards are scored and sized, and durable-memory guidance.")]
    public async Task<string> GetProjectContextAsync(CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(project.Id, cancellationToken);
        // Read from the project's own workflows rather than from a fixed pair of files: the set of card types
        // is project data, and an agent that never learns about a type cannot create one.
        var workflows = (await definitions.ReadAsync(project.Id, cancellationToken)).Workflows;
        var initialization = await DescribeInitializationAsync(project.RootPath, cancellationToken);

        return $"""
            # Aiko project context

            Project: {project.Name}
            Project ID: {project.Id}
            Root: {project.RootPath}

            Work happens inside a stage execution, not beside the pipeline. A card in its backlog stage has no
            work in it yet: do not create or change files for a card before you have started the stage you are
            working in with aiko_start_stage - the start moves the card into that stage and leaves the record
            of the work. Do what the stage's instruction asks for, produce its required artifacts, and complete
            it with aiko_complete_stage; only then does the card move on. aiko_move_card advances a card one
            stage at a time and refuses to leave a stage that was never run, because work done outside a stage
            leaves no execution, no artifacts and no history.

            Before changing files, read the selected card and its current stage instruction.
            A stage's beforeSkills are what to invoke before you read its instruction, and its afterSkills
            are what to invoke once the instruction is done.
            Treat declaredScopeFiles as guidance. Warn before intentionally changing files outside it,
            and report actualChangedFiles when completing work.
            Use aiko_store_memory for durable decisions, conventions and lessons.

            ## How this project scores and sizes a card

            {DescribePriority(priority)}

            {DescribeCardTypes(workflows)}
            {initialization}
            """;
    }

    /// <summary>
    /// The project's own copy of what its template asked for before work starts, or an empty string.
    /// </summary>
    /// <remarks>
    /// Read from the project rather than from the template it came from: the project owns the copy, so editing
    /// a template never changes what an existing project was asked to become. A project created from a
    /// template without an instruction has no file, and then this contributes nothing at all.
    /// </remarks>
    private static async ValueTask<string> DescribeInitializationAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = AikoProjectPaths.InitializationDocument(projectRoot);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var instruction = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        return instruction.Length == 0
            ? string.Empty
            : $"""

            ## Project initialization instruction

            The template this project was created from asks for the following before work starts. Carry it out
            if it is not done yet, report what you created or changed, and do nothing when the project already
            matches - then say so.

            {instruction}
            """;
    }

    /// <summary>
    /// The card types the project defines, one section per type, each with the stages of its pipeline.
    /// </summary>
    /// <remarks>
    /// A card type is the workflow of the same name, so this is the project's own pipelined data rendered for
    /// an agent: it is what makes a type the user added in the workflow editor usable without a code change.
    /// </remarks>
    /// <param name="workflows">The project's workflows, each one a card type.</param>
    private static string DescribeCardTypes(IReadOnlyList<WorkflowDefinition> workflows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Card types of this project");
        builder.AppendLine();
        if (workflows.Count == 0)
        {
            builder.AppendLine("No workflow is defined, so this project has no card type yet.");
            return builder.ToString().TrimEnd();
        }

        builder.Append("A card's kind is the type id below and its workflowId is the same id lower-cased; a ")
            .Append("new card starts in the \"").Append(WorkflowDefinition.BacklogStageId)
            .AppendLine("\" stage of its own pipeline.");
        builder.AppendLine();

        foreach (var workflow in workflows.OrderBy(item => item.Title, StringComparer.Ordinal))
        {
            builder.Append("### ").Append(workflow.CardType)
                .Append(" (workflowId: ").Append(workflow.Id).Append(')');
            if (!string.IsNullOrWhiteSpace(workflow.Description))
            {
                builder.Append(" - ").Append(workflow.Description.Trim());
            }

            builder.AppendLine();
            foreach (var stage in workflow.Stages.OrderBy(item => item.Order))
            {
                builder.Append("- ").Append(stage.Id).Append(" \"").Append(stage.Title).Append('"');
                if (WorkflowDefinition.IsBacklog(stage))
                {
                    builder.Append(" (backlog: a new card starts here)");
                }

                if (!string.IsNullOrWhiteSpace(stage.Instruction))
                {
                    builder.Append(": ").Append(stage.Instruction.Trim());
                }

                if (stage.RequiredArtifacts.Count > 0)
                {
                    builder.Append(" Required artifacts: ")
                        .Append(string.Join(", ", stage.RequiredArtifacts.Select(artifact => artifact.Path)))
                        .Append('.');
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The scoring rules as an agent can act on them: which criteria to score and what to look at for each,
    /// then the size steps to choose from (ТЗ §10). Both are project content - the agent assigns the values
    /// and the size, so it has to read the tables rather than guess them.
    /// </summary>
    private static string DescribePriority(PrioritySettings priority)
    {
        var builder = new StringBuilder();
        if (priority.Criteria.Count == 0)
        {
            builder.AppendLine("No criteria are configured: a card keeps the own priority it was created with.");
        }
        else
        {
            builder.AppendLine("Score the criteria you have evidence for; the rest stay unscored:");
            foreach (var criterion in priority.Criteria)
            {
                builder.Append("- ").Append(criterion.Id)
                    .Append(" \"").Append(criterion.Title).Append('"')
                    .Append(" range ").Append(criterion.Minimum).Append("..").Append(criterion.Maximum)
                    .Append(" weight ").Append(criterion.Weight);
                var guidance = string.IsNullOrWhiteSpace(criterion.AiInstruction)
                    ? criterion.Description
                    : criterion.AiInstruction;
                if (!string.IsNullOrWhiteSpace(guidance))
                {
                    builder.Append(" - ").Append(guidance);
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine();
        if (priority.Grid.Count == 0)
        {
            builder.AppendLine("No size grid is configured: cards carry no size and no coefficient applies.");
        }
        else
        {
            builder.AppendLine("Assign the card a size step; the coefficient multiplies its score:");
            foreach (var size in priority.Grid)
            {
                builder.Append("- ").Append(size.Id).Append(" (x").Append(size.Coefficient).Append(')');
                if (!string.IsNullOrWhiteSpace(size.Description))
                {
                    builder.Append(" - ").Append(size.Description);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    [McpServerTool(Name = "aiko_open_ui", Title = "Open Aiko UI")]
    [Description(
        "Returns the local UI URL for the current project and optional card. The caller may open it for the user.")]
    public string OpenUi(
        [Description("Optional card id to select. Pass null for the project board.")]
        string? cardId)
    {
        var context = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HTTP request context is available.");
        var query = $"project={Uri.EscapeDataString(GetProjectId())}";
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            query += $"&card={Uri.EscapeDataString(cardId)}";
        }

        return $"{context.Request.Scheme}://{context.Request.Host}/?{query}";
    }
}
