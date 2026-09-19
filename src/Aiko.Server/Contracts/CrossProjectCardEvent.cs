namespace Aiko.Server.Contracts;

/// <summary>
/// Payload of a cross-project card-created event: what left the project, and where it went.
/// </summary>
/// <param name="CardId">Id of the card created in the target project.</param>
/// <param name="TargetProjectId">Id of the project the card was created in.</param>
internal sealed record CrossProjectCardEvent(string CardId, string TargetProjectId);
