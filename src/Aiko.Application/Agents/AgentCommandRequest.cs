using Aiko.Domain.Execution;

namespace Aiko.Application.Agents;

/// <summary>What a command needs from the agent that takes it.</summary>
/// <param name="Action">Command action.</param>
/// <param name="CardId">Card the command is about, or null for a project-wide action.</param>
/// <param name="StageId">Stage to run, when the action needs one.</param>
/// <param name="CardTypeId">Card type, when the caller knows it.</param>
/// <param name="RequestedAgentAdapterId">Agent a person named, or null when any agent may take the work.</param>
/// <param name="DefaultAgentAdapterId">Preferred agent, or null.</param>
/// <param name="RequiresAikoAccess">Whether the agent must be able to reach Aiko.</param>
/// <param name="RequestedAgentId">Configured agent instance a person named, or null.</param>
/// <param name="DefaultAgentId">Preferred agent instance, or null.</param>
public sealed record AgentCommandRequest(
    CardCommandAction Action,
    string? CardId = null,
    string? StageId = null,
    string? CardTypeId = null,
    string? RequestedAgentAdapterId = null,
    string? DefaultAgentAdapterId = null,
    bool RequiresAikoAccess = true,
    string? RequestedAgentId = null,
    string? DefaultAgentId = null);
