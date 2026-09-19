namespace Aiko.Server.Contracts;

/// <summary>
/// Requests of the command queue's state transitions. Placing a command has no request of its own here:
/// <see cref="Aiko.Application.Contracts.PlaceCommandRequest"/> is what the store takes, and a second shape
/// that had to be mapped onto it would be one more thing to keep in step.
/// </summary>
/// <param name="AgentAdapterId">Agent taking the command.</param>
internal sealed record ClaimCommandRequest(string AgentAdapterId);

/// <param name="Message">What the agent reported: the outcome, or the reason it failed.</param>
internal sealed record CommandReportRequest(string? Message);

/// <param name="Reason">Why the command was withdrawn, or null for the store's own wording.</param>
internal sealed record CancelCommandRequest(string? Reason);
