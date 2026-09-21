namespace Aiko.Application.Agents;

/// <summary>
/// Chooses which agent adapters a user-scope installation targets: the identifiers the caller named with
/// <c>--agent</c>, or - when none were named - the adapters whose own discovery found an installation.
/// </summary>
/// <remarks>
/// The rule lives in the contract layer rather than in the CLI because a live
/// <c>aiko agent install --scope user</c> writes into the user's own agent configuration: the choice has to be
/// verifiable by a spec that controls discovery, not by a command that touches somebody's home directory.
/// The discovery is the one the CLI already uses elsewhere (<c>agent list</c>, <c>repair</c>), so "the agent is
/// installed" keeps a single definition in the codebase.
/// Finding nobody stays an empty choice: creating a configuration for every adapter Aiko knows is the
/// behaviour this replaces, and writing files into agents that are not on the machine is exactly what it did
/// wrong.
/// </remarks>
public static class AgentSelection
{
    /// <summary>
    /// Splits an <c>--agent</c> value into identifiers; empty when the option was absent or blank.
    /// </summary>
    /// <param name="requestedAgents">Raw <c>--agent</c> value, for example <c>claude-code,codex</c>.</param>
    public static IReadOnlyList<string> Parse(string? requestedAgents) =>
        (requestedAgents ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The adapter identifiers to install for: the requested ones when <paramref name="requestedAgents"/> names
    /// any, and otherwise every adapter whose discovery found an installation.
    /// </summary>
    /// <param name="adapters">Adapters Aiko knows, in the order the caller wants them acted on.</param>
    /// <param name="requestedAgents">Raw <c>--agent</c> value; null or blank asks for discovery.</param>
    /// <param name="cancellationToken">Cancellation token for the discovery.</param>
    public static async ValueTask<IReadOnlyList<string>> ResolveAsync(
        IEnumerable<IAgentAdapter> adapters,
        string? requestedAgents,
        CancellationToken cancellationToken)
    {
        var requested = Parse(requestedAgents);
        if (requested.Count > 0)
        {
            return requested;
        }

        var installed = new List<string>();
        foreach (var adapter in adapters)
        {
            var installations = await adapter.DetectInstallationsAsync(cancellationToken);
            if (installations.Count > 0)
            {
                installed.Add(adapter.Id);
            }
        }

        return installed;
    }
}
