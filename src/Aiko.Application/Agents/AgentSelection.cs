namespace Aiko.Application.Agents;

/// <summary>
/// Chooses which agent adapters a user-scope installation targets: the identifiers the caller named with
/// <c>--agent</c>, or - when none were named - the adapters whose own discovery found an installation.
/// </summary>
/// <remarks>
/// The rule lives in the contract layer rather than in the CLI because a live
/// <c>aiko agent install --scope user</c> writes into the user's own agent configuration: the choice has to be
/// verifiable by a spec that controls discovery. The discovery is the one the CLI already uses elsewhere
/// (<c>agent list</c>, <c>repair</c>).
/// <para>
/// Finding nobody stays an empty choice: writing files into agents that are not on the machine is what the
/// behaviour this replaces did wrong.
/// </para>
/// </remarks>
public static class AgentSelection
{
    /// <summary>Splits an <c>--agent</c> value into identifiers; empty when the option was absent or blank.</summary>
    /// <param name="requestedAgents">Raw <c>--agent</c> value, for example <c>claude-code,codex</c>.</param>
    public static IReadOnlyList<string> Parse(string? requestedAgents) =>
        (requestedAgents ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The adapter identifiers to install for: the requested ones when <paramref name="requestedAgents"/> names
    /// any, and otherwise every adapter whose discovery found an installation.
    /// </summary>
    /// <remarks>
    /// The adapters are walked in the order the caller wants them acted on; a null or blank
    /// <paramref name="requestedAgents"/> asks for discovery.
    /// </remarks>
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
