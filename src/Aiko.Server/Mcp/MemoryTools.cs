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
    IMemoryStore memory) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_search_memory", Title = "Search Aiko memory")]
    [Description("Searches durable project Markdown memory. Get project context first.")]
    public async Task<string> SearchMemoryAsync(
        [Description("Case-insensitive text query.")]
        string query,
        [Description("Maximum result count from 1 to 100.")]
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await memory.SearchAsync(
            GetProjectId(),
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
