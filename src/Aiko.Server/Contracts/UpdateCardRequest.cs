namespace Aiko.Server.Contracts;

/// <summary>
/// Card update request: title, priority, declared scope and size.
/// </summary>
/// <param name="Title">New title.</param>
/// <param name="OwnPriority">New own priority, before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">Complete declared scope list.</param>
/// <param name="ExpectedRevision">Revision the caller read.</param>
/// <param name="Size">Size step of the project's grid (ТЗ §10); null clears it.</param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null);
