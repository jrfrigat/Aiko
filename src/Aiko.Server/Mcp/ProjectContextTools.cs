using System.ComponentModel;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools describing the current project: context for planning work and the UI URL.
/// </summary>
[McpServerToolType]
internal sealed class ProjectContextTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(
        Name = "aiko_get_project_context",
        Title = "Get Aiko project context")]
    [Description(
        "Call this first. Returns the current project, workflow instructions and durable-memory guidance.")]
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

        return $"""
            # Aiko project context

            Project: {project.Name}
            Project ID: {project.Id}
            Root: {project.RootPath}

            Before changing files, read the selected card and its current stage instruction.
            Treat declaredScopeFiles as guidance. Warn before intentionally changing files outside it,
            and report actualChangedFiles when completing work.
            Use aiko_store_memory for durable decisions, conventions and lessons.

            ## Story workflow
            {storyWorkflow}

            ## Task workflow
            {taskWorkflow}
            """;
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
