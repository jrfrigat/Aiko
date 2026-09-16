using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Server.Contracts;

namespace Aiko.Server.Mcp;

/// <summary>
/// Shared plumbing for the Aiko MCP tools: resolves the project from the projectId
/// route value of the /mcp/projects/{projectId} endpoint and parses tool arguments.
/// </summary>
internal abstract class ProjectToolBase(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects)
{
    /// <summary>
    /// Access to the current HTTP context carrying the projectId route value.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    /// <summary>
    /// Resolves the identifier of the current project from the MCP route.
    /// </summary>
    protected string GetProjectId()
    {
        var value = HttpContextAccessor.HttpContext?.Request.RouteValues["projectId"]?.ToString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("The MCP endpoint has no projectId route value.")
            : value;
    }

    /// <summary>
    /// Loads the registered project of the current MCP route.
    /// </summary>
    protected async ValueTask<RegisteredProject> GetProjectAsync(
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        return await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
    }

    /// <summary>
    /// Parses the card kind argument ("story" or "task").
    /// </summary>
    protected static CardKind ParseCardKind(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "story" => CardKind.Story,
            "task" => CardKind.Task,
            _ => throw new ArgumentException(
                "Card kind must be 'story' or 'task'.",
                nameof(value))
        };

    /// <summary>
    /// Parses an agent attempt state argument, for example "rate-limited".
    /// </summary>
    protected static AgentAttemptState ParseAgentState(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "queued" => AgentAttemptState.Queued,
            "running" => AgentAttemptState.Running,
            "waiting-for-user" => AgentAttemptState.WaitingForUser,
            "rate-limited" => AgentAttemptState.RateLimited,
            "paused" => AgentAttemptState.Paused,
            "failed" => AgentAttemptState.Failed,
            "cancelled" => AgentAttemptState.Cancelled,
            "superseded" => AgentAttemptState.Superseded,
            "completed" => AgentAttemptState.Completed,
            _ => throw new ArgumentException($"Unknown agent state: {value}", nameof(value))
        };

    /// <summary>
    /// Serializes a stage execution with the server JSON contract.
    /// </summary>
    protected static string SerializeExecution(StageExecution execution) =>
        JsonSerializer.Serialize(execution, ServerJsonContext.Default.StageExecution);

    /// <summary>
    /// Reads a text file, returning "(not configured)" when it does not exist.
    /// </summary>
    protected static async ValueTask<string> ReadOptionalTextAsync(
        string path,
        CancellationToken cancellationToken) =>
        File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : "(not configured)";
}
