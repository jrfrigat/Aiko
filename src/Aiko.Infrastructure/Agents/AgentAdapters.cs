using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// The agent adapters Aiko ships with, named in one place.
/// </summary>
/// <remarks>
/// This list is a promise the product makes about which agents Aiko connects, and more than one host has to
/// keep it: the daemon registers an adapter per entry, the CLI builds its installer from them and the
/// installer executable hands them to the repair. Three copies of five names is how one host comes to miss
/// an adapter the others have - the screens would offer an agent that the repair never rewrites, or a
/// command would land in one adapter and not its neighbour - so the list lives here and they all read it.
/// <para>
/// Instances rather than types, because the callers differ in what they need: a container takes the type of
/// each entry, while the installer passes the objects straight to the engine. Deriving the types from the
/// instances keeps this one list instead of two that could disagree.
/// </para>
/// </remarks>
public static class AgentAdapters
{
    /// <summary>
    /// One fresh instance of every built-in adapter, in the order the screens show them.
    /// </summary>
    /// <remarks>
    /// A method rather than a cached list: an adapter holds the paths and the detection it derives from the
    /// machine it runs on, and a host that asks for them asks once, at its own composition time.
    /// </remarks>
    public static IReadOnlyList<IAgentAdapter> CreateBuiltIn() =>
    [
        new ClaudeCodeAgentAdapter(),
        new CodexAgentAdapter(),
        new CursorAgentAdapter(),
        new ZCodeAgentAdapter(),
        new ClineAgentAdapter(),
        new OpenCodeAgentAdapter()
    ];
}
