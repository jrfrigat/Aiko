namespace Aiko.Server.Contracts;

/// <summary>
/// Request to rename a card type: the workflow's id changes, and with it the folder its cards are filed
/// in and the board section that shows them.
/// </summary>
/// <param name="NewId">The id the workflow should carry.</param>
internal sealed record RenameWorkflowRequest(string NewId);
