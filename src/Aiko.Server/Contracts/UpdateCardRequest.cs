namespace Aiko.Server.Contracts;

/// <summary>
/// Card update request: title, priority, declared scope, size and the criterion values.
/// </summary>
/// <param name="Title">New title.</param>
/// <param name="OwnPriority">New own priority, before the size coefficient.</param>
/// <param name="DeclaredScopeFiles">Complete declared scope list.</param>
/// <param name="ExpectedRevision">Revision the caller read.</param>
/// <param name="Size">Size step of the project's grid (ТЗ §10); null clears it.</param>
/// <param name="CriterionValues">
/// The card's scores per criterion, keyed by criterion id, or null to leave the stored values untouched.
/// A form that edits the criteria sends the whole set, so a criterion left out of the dictionary is one
/// the card holds no value for - which is how a cleared field is saved (ТЗ §10).
/// </param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null);
