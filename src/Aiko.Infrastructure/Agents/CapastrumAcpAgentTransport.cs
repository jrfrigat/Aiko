using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Capastrum.Acp;
using Capastrum.Acp.Protocol;

namespace Aiko.Infrastructure.Agents;

/// <summary>Delivers a command by starting an ACP session and sending it the stage prompt.</summary>
public sealed class CapastrumAcpAgentTransport(
    IProjectCatalog projects,
    IAgentMcpEndpoint mcpEndpoints,
    IAgentDeliveryStore deliveries,
    IAcpClientFactory clients) : IAgentTransport
{
    /// <inheritdoc />
    public AgentTransport Transport => AgentTransport.Acp;

    /// <inheritdoc />
    public async ValueTask<AgentTransportResult> DeliverAsync(
        AgentDeliveryContext context,
        CancellationToken cancellationToken)
    {
        var acp = context.Agent.Acp;
        if (acp?.Executable is not { Length: > 0 } executable)
        {
            return new AgentTransportResult(false, Message: "ACP executable is not configured.");
        }

        var project = await projects.FindAsync(context.ProjectId, cancellationToken);
        if (project is null)
        {
            return new AgentTransportResult(
                false,
                Message: $"Unknown project '{context.ProjectId}'.");
        }

        var endpoint = await mcpEndpoints.GetAsync(
            project.Id,
            context.Delivery.Id,
            cancellationToken);
        var root = Path.GetFullPath(project.RootPath);
        var updates = new List<string>();
        var permissions = new List<string>();
        string? sessionId = null;

        try
        {
            await using var client = await clients.StartAsync(
                new AcpLaunchOptions(
                    executable,
                    acp.Args ?? [],
                    root,
                    acp.Environment,
                    text =>
                    {
                        lock (updates)
                        {
                            if (updates.Count < 200)
                            {
                                updates.Add(text);
                            }
                            else
                            {
                                updates[^1] = text;
                            }
                        }
                    },
                    (path, token) => ReadAsync(root, path, token),
                    (path, content, token) => WriteAsync(root, path, content, token),
                    (request, _) =>
                    {
                        var decision = AgentPermissionPolicy.Decide(root, request.Command);
                        lock (permissions)
                        {
                            if (permissions.Count < 50)
                            {
                                permissions.Add(
                                    $"{(ReferenceEquals(decision, ConfirmationResult.Denied) ? "denied" : "allowed")}: " +
                                    $"{request.Title ?? request.ToolName ?? "tool"}");
                            }
                        }

                        return ValueTask.FromResult(decision);
                    }),
                cancellationToken);

            sessionId = await client.NewSessionAsync(
                [AikoMcpServer(endpoint)],
                cancellationToken);
            await deliveries.UpdateAsync(
                context.ProjectId,
                context.Delivery with
                {
                    SessionId = sessionId,
                    State = AgentDeliveryState.Running,
                    StartedAtUtc = context.Delivery.StartedAtUtc ?? DateTimeOffset.UtcNow
                },
                cancellationToken);

            var response = await client.SendPromptAsync(
                sessionId,
                BuildPrompt(context),
                cancellationToken);
            var succeeded = response.StopReason is AcpStopReason.EndTurn;
            var message = succeeded
                ? LastText(updates)
                : $"ACP stopped with {response.StopReason}.";
            var permissionText = LastText(permissions);
            if (permissionText is { Length: > 0 })
            {
                message = string.IsNullOrEmpty(message)
                    ? permissionText
                    : $"{message} ({permissionText})";
            }

            try
            {
                await client.CloseSessionAsync(sessionId, CancellationToken.None);
            }
            catch
            {
                // The session is already gone; the delivery result is what matters.
            }

            await RevokeAsync(mcpEndpoints, endpoint);
            return new AgentTransportResult(succeeded, sessionId, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RevokeAsync(mcpEndpoints, endpoint);
            throw;
        }
        catch (Exception exception)
        {
            await RevokeAsync(mcpEndpoints, endpoint);
            return new AgentTransportResult(false, sessionId, exception.Message);
        }
    }

    private static async ValueTask RevokeAsync(
        IAgentMcpEndpoint mcpEndpoints,
        AgentMcpEndpoint endpoint)
    {
        try
        {
            await mcpEndpoints.RevokeAsync(endpoint, CancellationToken.None);
        }
        catch
        {
            // A token that outlives the attempt is expired by its own lifetime; the result is what matters.
        }
    }

    private static AcpMcpServer AikoMcpServer(AgentMcpEndpoint endpoint) => new()
    {
        Name = "aiko",
        Type = "http",
        Url = endpoint.Url,
        Headers = endpoint.AccessToken is { Length: > 0 } token
            ? [new AcpHttpHeader { Name = "Authorization", Value = $"Bearer {token}" }]
            : null
    };

    private static string BuildPrompt(AgentDeliveryContext context)
    {
        var command = context.Command;
        var about = command.CardId is { Length: > 0 } card
            ? $"card {card}"
            : "the project";
        var stage = command.StageId is { Length: > 0 } stageId
            ? $" and stage {stageId}"
            : string.Empty;
        var text = command.Text is { Length: > 0 } reason
            ? $" The command says: {reason}"
            : string.Empty;
        return $"Aiko command {command.Id}: {command.Action} for {about}{stage}. " +
               "Carry it out through Aiko's MCP tools (aiko_start_stage, aiko_report_progress, " +
               "aiko_save_card_artifact, aiko_complete_stage, aiko_add_comment) and report the " +
               "result in Aiko." + text;
    }

    private static string? LastText(List<string> updates)
    {
        lock (updates)
        {
            return updates.Count == 0 ? null : updates[^1];
        }
    }

    private static async ValueTask<string?> ReadAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        var full = Confine(root, path);
        return File.Exists(full)
            ? await File.ReadAllTextAsync(full, cancellationToken)
            : null;
    }

    private static async ValueTask WriteAsync(
        string root,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var full = Confine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, cancellationToken);
    }

    private static string Confine(string root, string path)
    {
        var full = Path.GetFullPath(path, root);
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"ACP file access outside the project is not allowed: {path}");
        }

        return full;
    }
}
