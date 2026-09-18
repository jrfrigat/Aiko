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
/// <param name="Requirements">
/// What the card is asked to do, or null to leave the stored text untouched. An empty string clears it.
/// </param>
/// <param name="Request">
/// The original request, or null to leave the stored text untouched. A card that has left the backlog refuses
/// any change here, clearing included; null is the only accepted value for such a card.
/// </param>
/// <param name="RequirementsReason">
/// What the change came from, so the discussion can say why the description moved. Null writes the note without
/// the quote.
/// </param>
internal sealed record UpdateCardRequest(
    string Title,
    decimal OwnPriority,
    IReadOnlyList<string> DeclaredScopeFiles,
    long ExpectedRevision,
    string? Size = null,
    IReadOnlyDictionary<string, decimal>? CriterionValues = null,
    string? Requirements = null,
    string? Request = null,
    string? RequirementsReason = null);
