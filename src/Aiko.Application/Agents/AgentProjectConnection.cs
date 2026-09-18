namespace Aiko.Application.Agents;

/// <summary>
/// Whether one adapter is connected to one project.
/// </summary>
/// <remarks>
/// The project-scoped half of the agent list, and a different fact from the machine-wide connection: an agent
/// can be connected globally and still have nothing in a particular project. It is derived from the files an
/// install owns rather than recorded in a registry, because a list would be a second source of truth that
/// goes stale the moment someone deletes a file by hand or copies the project to another machine.
/// </remarks>
/// <param name="AdapterId">Adapter identifier, for example <c>claude-code</c>.</param>
/// <param name="DisplayName">Name to show for the adapter.</param>
/// <param name="Connected">Whether the files Aiko writes for this adapter are in the project.</param>
public sealed record AgentProjectConnection(
    string AdapterId,
    string DisplayName,
    bool Connected);
