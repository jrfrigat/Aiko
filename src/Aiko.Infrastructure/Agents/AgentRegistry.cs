using Aiko.Application.Agents;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Reads every agent's effective configuration and decides whether a command can be delivered directly or
/// has to wait in the project queue.
/// </summary>
public sealed class AgentRegistry(
    IEnumerable<IAgentAdapter> adapters,
    IAgentConfigService configs,
    IAgentTransportHealth health,
    IAgentSessionCounter sessions) : IAgentRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentAdapter> adaptersById = adapters
        .ToDictionary(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RegisteredAgent>> ListAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        var results = new List<RegisteredAgent>();
        foreach (var config in await configs.ListEffectiveAsync(projectId, cancellationToken))
        {
            if (!adaptersById.TryGetValue(config.AdapterId ?? config.Id, out var adapter))
            {
                continue;
            }

            var installations = await adapter.DetectInstallationsAsync(cancellationToken);
            var available = config.Enabled &&
                installations.Count > 0 &&
                await health.IsAvailableAsync(config, cancellationToken);
            var active = await sessions.CountAsync(config.Id, cancellationToken);
            results.Add(new RegisteredAgent(config, installations, available, active));
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<AgentRoute> ResolveAsync(
        string? projectId,
        AgentCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var agents = await ListAsync(projectId, cancellationToken);
        var refusals = new List<AgentCandidateRefusal>();
        var requestedId = request.RequestedAgentId ?? request.RequestedAgentAdapterId;

        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            var requested = Find(agents, requestedId);
            if (requested is null)
            {
                var unknown = $"Unknown agent '{requestedId.Trim()}'.";
                refusals.Add(new AgentCandidateRefusal(
                    requestedId.Trim(),
                    unknown));
                return AgentRoute.Queue(unknown, refusals);
            }

            var refusal = Refusal(requested, request);
            if (refusal is null)
            {
                return new AgentRoute(
                    requested.Config,
                    requested.Config.Transport,
                    true,
                    null,
                    []);
            }

            refusals.Add(new AgentCandidateRefusal(requested.Config.Id, refusal));
            return AgentRoute.Queue(refusal, refusals, requested.Config);
        }

        var preferredId = request.DefaultAgentId ?? request.DefaultAgentAdapterId;
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var preferred = Find(agents, preferredId);
            if (preferred is not null)
            {
                var refusal = Refusal(preferred, request);
                if (refusal is null)
                {
                    return new AgentRoute(
                        preferred.Config,
                        preferred.Config.Transport,
                        true,
                        null,
                        []);
                }

                refusals.Add(new AgentCandidateRefusal(preferred.Config.Id, refusal));
            }
        }

        var direct = agents
            .Select(agent => (Agent: agent, Refusal: Refusal(agent, request)))
            .Where(candidate => candidate.Refusal is null)
            .OrderByDescending(candidate => candidate.Agent.Config.Priority)
            .ThenBy(candidate => candidate.Agent.Config.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (direct.Agent is not null)
        {
            return new AgentRoute(
                direct.Agent.Config,
                direct.Agent.Config.Transport,
                true,
                null,
                refusals);
        }

        foreach (var agent in agents)
        {
            var refusal = Refusal(agent, request);
            if (refusal is not null)
            {
                refusals.Add(new AgentCandidateRefusal(agent.Config.Id, refusal));
            }
        }

        return AgentRoute.Queue(
            "No agent can take the command directly; it stays in the project queue.",
            refusals);
    }

    private static RegisteredAgent? Find(
        IReadOnlyList<RegisteredAgent> agents,
        string adapterId) =>
        agents.FirstOrDefault(agent =>
            StringComparer.OrdinalIgnoreCase.Equals(
                agent.Config.Id,
                adapterId.Trim()));

    private static string? Refusal(RegisteredAgent agent, AgentCommandRequest request)
    {
        var config = agent.Config;
        if (!config.Enabled)
        {
            return "the agent is disabled.";
        }

        if (!agent.IsInstalled)
        {
            return "the agent is not installed.";
        }

        if (!agent.IsAvailable)
        {
            return "the configured transport is not available.";
        }

        if (!config.CanDispatchDirectly)
        {
            return config.Transport == AgentTransport.Command
                ? "the agent is configured to take work from the queue."
                : "the transport is not supported by the adapter.";
        }

        if (agent.ActiveSessions >= config.Capabilities.MaxConcurrentSessions)
        {
            return "the agent has no free session slot.";
        }

        if (!Allows(config.Capabilities.Actions, ActionName(request.Action)))
        {
            return $"the agent does not carry out '{ActionName(request.Action)}'.";
        }

        if (request.StageId is { Length: > 0 } stage &&
            !Allows(config.Capabilities.Stages, stage))
        {
            return $"the agent does not run stage '{stage}'.";
        }

        if (request.CardTypeId is { Length: > 0 } cardType &&
            !Allows(config.Capabilities.CardTypes, cardType))
        {
            return $"the agent does not work cards of type '{cardType}'.";
        }

        if (request.RequiresAikoAccess &&
            (config.Capabilities.AikoAccess is null ||
             config.Capabilities.AikoAccess.Count == 0))
        {
            return "the agent has no way to reach Aiko, so it cannot report the stage.";
        }

        return null;
    }

    private static bool Allows(IReadOnlyList<string>? values, string value) =>
        values is not null &&
        values.Any(item =>
            item == "*" ||
            StringComparer.OrdinalIgnoreCase.Equals(item, value));

    private static string ActionName(CardCommandAction action) => action switch
    {
        CardCommandAction.Start => "start",
        CardCommandAction.Pause => "pause",
        CardCommandAction.Resume => "resume",
        CardCommandAction.Answer => "answer",
        CardCommandAction.RunBoard => "run-board",
        CardCommandAction.Release => "release",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown command action.")
    };
}
