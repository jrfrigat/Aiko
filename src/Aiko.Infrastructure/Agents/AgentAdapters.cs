using Aiko.Application.Agents;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// The agent adapters Aiko ships with, named in one place.
/// </summary>
/// <remarks>
/// This list is a promise the product makes about which agents Aiko connects, and more than one host has to
/// keep it: the daemon registers an adapter per entry, the CLI builds its installer from them and the
/// installer executable hands them to the repair. Separate copies of the names are how the screens come to
/// offer an agent the repair never rewrites, so the list lives here and they all read it. Instances rather
/// than types, because the callers differ in what they need; deriving one from the other keeps a single list
/// instead of two that could disagree.
/// </remarks>
public static class AgentAdapters
{
    /// <summary>
    /// One fresh instance of every built-in adapter, in the order the screens show them.
    /// </summary>
    /// <remarks>
    /// A method rather than a cached list: an adapter holds the paths and the detection it derives from the
    /// machine it runs on, and a host asks for them once, at its own composition time.
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
