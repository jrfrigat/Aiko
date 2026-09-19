namespace Aiko.Application.Agents;

/// <summary>
/// What to say about an agent that is on this machine but not connected to Aiko.
/// </summary>
/// <remarks>
/// An agent is routinely installed long after Aiko is, which is the ordinary case rather than an exotic
/// one. The answer is the same wherever it is printed - the <c>status</c> command, a notice in the
/// interface - so both the wording and the command live here instead of being written out again at every
/// call site. Detection comes from PATH and the connection from Aiko's own files, exactly as in the
/// dashboard: an agent whose files are all present is connected even if a later PATH change hid the
/// executable, and an agent that is not on PATH has nothing to connect to.
/// </remarks>
public static class AgentConnectionHint
{
    /// <summary>
    /// Whether the agent was found on PATH while Aiko's user-scope configuration is missing or partial.
    /// </summary>
    public static bool NeedsConnection(AgentAdapterOption option)
    {
        if (option.Installations.Count == 0)
        {
            return false;
        }

        return option.UserScope is not { } scope || !scope.IsConfigured();
    }

    /// <summary>The command that connects one agent machine-wide.</summary>
    public static string ConnectCommand(string adapterId) =>
        $"aiko agent install --agent {adapterId} --scope user";

    /// <summary>
    /// One line describing the state and the fix, or null when there is nothing to do about this agent.
    /// </summary>
    public static string? Describe(AgentAdapterOption option)
    {
        if (!NeedsConnection(option))
        {
            return null;
        }

        var state = option.UserScope is { } scope && scope.IsPartial()
            ? "partially connected"
            : "not connected";
        return $"{option.Id} ({option.DisplayName}) - {state}; connect with `{ConnectCommand(option.Id)}`";
    }
}
