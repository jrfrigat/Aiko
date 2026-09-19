using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for the command queue: what a screen placed for an agent, and the two calls that report back.
/// </summary>
/// <remarks>
/// This is how a request that started on the board reaches an agent that does not exist yet. The agent reads
/// the queue on its own run, takes one command, carries it out with the execution tools it already has, and
/// closes it - so the queue never becomes a second way to do the work, only a way to be asked for it.
/// </remarks>
[McpServerToolType]
internal sealed class CommandTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    ICardCommandStore commands) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_commands", Title = "List Aiko commands")]
    [Description(
        "Lists the commands a screen placed for an agent. These are things the person asked for while no "
        + "agent was running: the agent takes one with aiko_claim_command, carries it out, and closes it. "
        + "Read this at the start of a run when the user says the board has work waiting.")]
    public async Task<string> ListCommandsAsync(
        [Description(
            "Which commands to read: 'open' (default) for the ones nobody has closed, 'all' for the history "
            + "too, or a state name - queued, taken, completed, failed, cancelled.")]
        string? state = null,
        [Description("Only commands for this card, or null for the whole project.")]
        string? cardId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = state?.Trim();
        var openOnly = string.IsNullOrEmpty(trimmed) ||
                       StringComparer.OrdinalIgnoreCase.Equals(trimmed, "open");
        var entries = await commands.ListAsync(GetProjectId(), !openOnly, cancellationToken);

        if (openOnly is false &&
            !StringComparer.OrdinalIgnoreCase.Equals(trimmed, "all"))
        {
            var wanted = CardCommands.ParseState(trimmed!);
            entries = [.. entries.Where(entry => entry.State == wanted)];
        }

        if (!string.IsNullOrWhiteSpace(cardId))
        {
            var wanted = cardId.Trim();
            entries = [.. entries.Where(entry =>
                StringComparer.Ordinal.Equals(entry.CardId, wanted))];
        }

        return JsonSerializer.Serialize(
            entries,
            ServerJsonContext.Default.IReadOnlyListCardCommand);
    }

    [McpServerTool(Name = "aiko_claim_command", Title = "Take Aiko command")]
    [Description(
        "Takes one command from the queue for this run. A command another agent already took is refused, and "
        + "so is one that named a different agent - leave those alone and say so. Read the command's action "
        + "and carry it out with the tool it names; an action that reads RunBoard asks for the whole board "
        + "rather than one card, and the /aiko-run-all procedure is what carries that out. Then close it with "
        + "aiko_finish_command.")]
    public async Task<string> ClaimCommandAsync(
        [Description("Command id, for example CMD-1.")]
        string commandId,
        [Description("Agent adapter id, for example cline.")]
        string agentAdapterId,
        CancellationToken cancellationToken)
    {
        var command = await commands.ClaimAsync(
            GetProjectId(),
            commandId,
            agentAdapterId,
            cancellationToken);
        return JsonSerializer.Serialize(command, ServerJsonContext.Default.CardCommand);
    }

    [McpServerTool(Name = "aiko_finish_command", Title = "Close Aiko command")]
    [Description(
        "Closes a command this run has taken: completed when the work the command asked for has been done, "
        + "failed when it could not be. Never leave a taken command open - the queue is what the person reads "
        + "to see whether their request happened, and the message is what they read.")]
    public async Task<string> FinishCommandAsync(
        [Description("Command id, for example CMD-1.")]
        string commandId,
        [Description("State: completed or failed.")]
        string state,
        [Description("What happened, or why it failed. This is what the person reads on the card.")]
        string? message,
        CancellationToken cancellationToken)
    {
        var command = await commands.FinishAsync(
            GetProjectId(),
            commandId,
            CardCommands.ParseState(state),
            message,
            cancellationToken);
        return JsonSerializer.Serialize(command, ServerJsonContext.Default.CardCommand);
    }
}
