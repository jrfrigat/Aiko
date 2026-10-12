using System.Text.Json;
using Microsoft.JSInterop;

namespace Aiko.Pwa.Services;

/// <summary>
/// A card the person has started and not created yet. Aiko names a card and the daemon owns it, so a
/// half-filled form is nobody's card: it is the text in front of the person, and losing it to a reload or a
/// click on the board is the only thing this type exists to prevent.
/// </summary>
/// <param name="Title">The title as it was typed.</param>
/// <param name="Requirements">What the card is asked to do, as it was typed.</param>
/// <param name="Scope">The declared scope, one pattern per line.</param>
/// <param name="Kind">The card type the form was on.</param>
/// <param name="Size">The size step chosen, if any.</param>
/// <param name="Priority">The hand-entered priority, for a project that scores by hand.</param>
/// <param name="CriterionValues">The score typed for each criterion that was filled in; a criterion left empty is absent.</param>
/// <param name="SavedAt">When the draft was last written, so an old one can be told from a fresh one.</param>
public sealed record NewCardDraft(
    string? Title,
    string? Requirements,
    string? Scope,
    string? Kind,
    string? Size,
    decimal? Priority,
    Dictionary<string, decimal> CriterionValues,
    DateTimeOffset SavedAt);

/// <summary>
/// The new-card form's draft, kept in the browser's own storage under a key that names the project.
/// </summary>
/// <remarks>
/// The screen owns the draft rather than a container: the page is what knows which project it is creating
/// in, and the key is built from that project, so one project's draft can never surface in another. Storage
/// is the browser's and it can refuse - private mode throws on access - so a refused write costs the memory
/// of the draft and nothing else: the form keeps working and the guard still asks before the text goes.
/// </remarks>
public sealed class CardDraftStore(IJSRuntime js)
{
    /// <summary>
    /// The prefix of the key a project's draft lives under. The project is part of the key, which is what
    /// keeps two projects from sharing one draft.
    /// </summary>
    private const string KeyPrefix = "aiko-new-card-draft:";

    /// <summary>
    /// What the last visit left behind, or null when there is nothing to come back to. A draft the current
    /// shape of this type cannot read is dropped rather than half-read into the form.
    /// </summary>
    public async Task<NewCardDraft?> ReadAsync(string projectId)
    {
        var stored = await ReadRawAsync(projectId);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NewCardDraft>(stored, PwaJson.Options);
        }
        catch (JsonException)
        {
            await ClearAsync(projectId);
            return null;
        }
    }

    /// <summary>Writes the draft, replacing whatever was there.</summary>
    public async Task SaveAsync(string projectId, NewCardDraft draft)
    {
        try
        {
            await js.InvokeVoidAsync(
                "localStorage.setItem",
                Key(projectId),
                JsonSerializer.Serialize(draft, PwaJson.Options));
        }
        catch (JSException)
        {
            // Storage refused: the draft is not remembered, and the form is none the wiser.
        }
        catch (InvalidOperationException)
        {
            // No JavaScript runtime at this point of the page's life; there is nowhere to write yet.
        }
    }

    /// <summary>Forgets the draft. Called when the card it was for has been created, or when it was discarded on purpose.</summary>
    public async Task ClearAsync(string projectId)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", Key(projectId));
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task<string?> ReadRawAsync(string projectId)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Key(projectId));
        }
        catch (JSException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Key(string projectId) => $"{KeyPrefix}{projectId}";
}
