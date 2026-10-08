using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for the durable project memory: searching and storing
/// Markdown knowledge under .aiko/memory.
/// </summary>
[McpServerToolType]
internal sealed class MemoryTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IMemoryStore memory,
    IMemorySemanticSearch semantic) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_search_memory", Title = "Search Aiko memory")]
    [Description("Searches durable project Markdown memory. Get project context first.")]
    public async Task<string> SearchMemoryAsync(
        [Description("Case-insensitive text query.")]
        string query,
        [Description("Maximum result count from 1 to 100.")]
        int limit,
        [Description(
            "Ranking to use: 'fts' (word matches, the default) or 'semantic' (matches by meaning; needs the " +
            "module on for the project and its model installed).")]
        string? mode,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();

        // 'semantic' is answered or refused, never quietly redirected to the full-text search: a caller that
        // asked for meaning and got word matches would be reading the right list for the wrong question.
        if (string.Equals(mode, "semantic", StringComparison.OrdinalIgnoreCase))
        {
            var hits = await semantic.SearchAsync(projectId, query, limit, cancellationToken);
            return JsonSerializer.Serialize(
                hits,
                ServerJsonContext.Default.IReadOnlyListMemorySearchHit);
        }

        if (!string.IsNullOrWhiteSpace(mode) &&
            !string.Equals(mode, "fts", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown search mode '{mode}'. Use 'fts' or 'semantic'.", nameof(mode));
        }

        var result = await memory.SearchAsync(
            projectId,
            query,
            limit,
            cancellationToken);
        return JsonSerializer.Serialize(
            result,
            ServerJsonContext.Default.IReadOnlyListMemoryDocument);
    }

    [McpServerTool(Name = "aiko_store_memory", Title = "Store Aiko memory")]
    [Description(
        "Stores durable Markdown knowledge under .aiko/memory. Use for decisions, conventions and lessons.")]
    public async Task<string> StoreMemoryAsync(
        [Description("Relative Markdown path, for example decisions/auth.md.")]
        string path,
        [Description("Complete Markdown content.")]
        string content,
        CancellationToken cancellationToken)
    {
        await memory.StoreAsync(GetProjectId(), path, content, cancellationToken);
        return $"Stored durable memory '{path}'.";
    }
}
