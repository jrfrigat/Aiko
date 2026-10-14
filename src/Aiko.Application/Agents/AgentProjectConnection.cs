namespace Aiko.Application.Agents;

/// <summary>Whether one adapter is connected to one project.</summary>
/// <remarks>
/// A different fact from the machine-wide connection: an agent can be connected globally and still have nothing
/// in a particular project. Derived from the files an install owns rather than recorded in a registry, because
/// a list would go stale the moment someone deletes a file by hand or copies the project to another machine.
/// </remarks>
public sealed record AgentProjectConnection(
    string AdapterId,
    string DisplayName,
    bool Connected);
