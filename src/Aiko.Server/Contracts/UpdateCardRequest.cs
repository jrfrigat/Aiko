namespace Aiko.Server.Contracts;

/// <summary>
/// Card update request: title, priority and declared scope.
/// </summary>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision);
