using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Prioritization;
using Aiko.Infrastructure.Storage;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools describing the current project: context for planning work and the UI URL.
/// </summary>
[McpServerToolType]
internal sealed class ProjectContextTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IAppSettingsService settings) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(
        Name = "aiko_get_project_context",
        Title = "Get Aiko project context")]
    [Description(
        "Call this first. Returns the current project, how its cards are scored and sized, the workflow "
        + "instructions and durable-memory guidance.")]
    public async Task<string> GetProjectContextAsync(CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(cancellationToken);
        var stitchRoot = AikoProjectPaths.DataRoot(project.RootPath);
        var storyWorkflow = await ReadOptionalTextAsync(
            Path.Combine(stitchRoot, "workflows", "story.json"),
            cancellationToken);
        var taskWorkflow = await ReadOptionalTextAsync(
            Path.Combine(stitchRoot, "workflows", "task.json"),
            cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(project.Id, cancellationToken);

        return $"""
            # Aiko project context

            Project: {project.Name}
            Project ID: {project.Id}
            Root: {project.RootPath}

            Before changing files, read the selected card and its current stage instruction.
            Treat declaredScopeFiles as guidance. Warn before intentionally changing files outside it,
            and report actualChangedFiles when completing work.
            Use aiko_store_memory for durable decisions, conventions and lessons.

            ## How this project scores and sizes a card

            {DescribePriority(priority)}

            ## Story workflow
            {storyWorkflow}

            ## Task workflow
            {taskWorkflow}
            """;
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
