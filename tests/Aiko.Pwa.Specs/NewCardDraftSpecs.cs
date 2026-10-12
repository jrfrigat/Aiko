using System.Xml.Linq;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// A card that is half written is the person's work, not the daemon's: it is not thrown away by a click, it
/// comes back after a reload, and it is discarded only when it was discarded on purpose. Read from the
/// markup and the code, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </summary>
public sealed class NewCardDraftSpecs
{
    [Fact]
    public void A_half_filled_card_is_not_lost_by_clicking_away()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "NewCardPage.razor");

        // Leaving the page asks first, the way the card inspector, the artifact editor and the memory page ask.
        Assert.Contains("<NavigationLock", page, StringComparison.Ordinal);
        Assert.Contains("ConfirmExternalNavigation=\"@IsDirty\"", page, StringComparison.Ordinal);
        Assert.Contains("OnBeforeInternalNavigation=\"ConfirmNavigationAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("private bool IsDirty", page, StringComparison.Ordinal);

        var navigation = Method(page, "private async Task ConfirmNavigationAsync(LocationChangingContext context)");
        Assert.Contains("Loc.Get(\"LeaveWithUnsavedEdits\")", navigation, StringComparison.Ordinal);
        Assert.Contains("context.PreventNavigation()", navigation, StringComparison.Ordinal);

        // The Cancel button asks rather than deciding: with text in the form it raises the question, and with
        // nothing typed it just leaves.
        Assert.DoesNotContain("OnClick=\"Cancel\"", page, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(page, "OnClick=\"AskLeave\""));
        var ask = Method(page, "private void AskLeave()");
        Assert.Contains("NavigateTo(BoardHref)", ask, StringComparison.Ordinal);
        Assert.Contains("_leaveAsk = true;", ask, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unfinished_card_comes_back_and_can_be_thrown_away()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "NewCardPage.razor");

        // The draft is read once per project, so another project's draft can never be put in this form.
        var parameters = Method(page, "protected override async Task OnParametersSetAsync()");
        Assert.Contains("_restoredProject", parameters, StringComparison.Ordinal);
        Assert.Contains("await RestoreDraftAsync();", parameters, StringComparison.Ordinal);
        var restore = Method(page, "private async Task RestoreDraftAsync()");
        Assert.Contains("await Drafts.ReadAsync(ProjectId)", restore, StringComparison.Ordinal);
        Assert.Contains("_draftRestored = true;", restore, StringComparison.Ordinal);

        // It says so, and offers the one way to say no: the explanation and the reset are both on the page.
        Assert.Contains("Loc.Get(\"NewCardDraftRestored\")", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"NewCardDraftStartOver\")", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"StartOverAsync\"", page, StringComparison.Ordinal);
        var startOver = Method(page, "private async Task StartOverAsync()");
        Assert.Contains("await Drafts.ClearAsync(ProjectId);", startOver, StringComparison.Ordinal);
        Assert.Contains("_draftRestored = false;", startOver, StringComparison.Ordinal);

        // Typing is remembered as it happens, and a keystroke is not a write of its own.
        var render = Method(page, "protected override async Task OnAfterRenderAsync(bool firstRender)");
        Assert.Contains("_writtenSnapshot", render, StringComparison.Ordinal);
        Assert.Contains("await Drafts.SaveAsync(ProjectId, CaptureDraft());", render, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_about_cancelling_keeps_or_discards_the_draft()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "NewCardPage.razor");

        Assert.Contains("<FlareDialog Visible=\"@_leaveAsk\"", page, StringComparison.Ordinal);
        foreach (var caption in new[]
                 {
                     "NewCardCancelTitle",
                     "NewCardCancelHint",
                     "NewCardCancelStay",
                     "NewCardCancelKeepDraft",
                     "NewCardCancelDiscard",
                 })
        {
            Assert.Contains($"Loc.Get(\"{caption}\")", page, StringComparison.Ordinal);
        }

        // Staying closes the question and touches nothing.
        Assert.Contains("private void StayOnForm() => _leaveAsk = false;", page, StringComparison.Ordinal);

        // Leaving with the draft keeps it: only the question is closed and the page changes.
        var keep = Method(page, "private void LeaveKeepingDraft()");
        Assert.Contains("NavigateTo(BoardHref)", keep, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearAsync", keep, StringComparison.Ordinal);

        // Leaving without it goes through the same reset the alert's button does.
        var discard = Method(page, "private async Task LeaveAndStartOverAsync()");
        Assert.Contains("await StartOverAsync();", discard, StringComparison.Ordinal);
    }

    [Fact]
    public void The_draft_goes_when_the_card_exists_and_stays_when_creation_fails()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "NewCardPage.razor");
        var create = Method(page, "private async Task CreateAsync()");

        var stored = create.IndexOf("EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var cleared = create.IndexOf("await Drafts.ClearAsync(ProjectId);", StringComparison.Ordinal);
        var failure = create.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.True(stored >= 0, "the create call is expected in CreateAsync");
        Assert.True(cleared > stored, "the draft is cleared only after the daemon took the card");
        Assert.True(failure > cleared, "the failure branch comes after the clear");

        // A refused or broken attempt keeps the text, so the retry is not typing it all again.
        var failureBranch = create[failure..];
        Assert.DoesNotContain("ClearAsync", failureBranch, StringComparison.Ordinal);
        Assert.Contains("ReportErrorAsync", failureBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_draft_lives_in_browser_storage_under_the_project_key()
    {
        var store = Read("src", "Aiko.Pwa", "Services", "CardDraftStore.cs");

        // Blazor addresses the browser's own storage, which is how the app already keeps its language choice.
        Assert.Contains("\"localStorage.setItem\"", store, StringComparison.Ordinal);
        Assert.Contains("\"localStorage.getItem\"", store, StringComparison.Ordinal);
        Assert.Contains("\"localStorage.removeItem\"", store, StringComparison.Ordinal);

        // The project is part of the key: one project's draft cannot surface in another.
        Assert.Contains("aiko-new-card-draft:", store, StringComparison.Ordinal);
        Assert.Contains("KeyPrefix", store, StringComparison.Ordinal);

        // Storage may refuse - private mode throws - which costs the draft's memory and nothing else.
        Assert.Contains("catch (JSException)", store, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", store, StringComparison.Ordinal);
    }

    [Fact]
    public void The_draft_type_is_registered_for_the_trimmed_client()
    {
        var context = Read("src", "Aiko.Pwa", "Services", "PwaJsonContext.cs");

        // The published client is trimmed and its JSON is source-generated: a type that is not declared here
        // cannot be written or read at all.
        Assert.Contains("[JsonSerializable(typeof(NewCardDraft))]", context, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(Dictionary<string, decimal>))]", context, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NewCardDraftRestored")]
    [InlineData("NewCardDraftStartOver")]
    [InlineData("NewCardCancelTitle")]
    [InlineData("NewCardCancelHint")]
    [InlineData("NewCardCancelStay")]
    [InlineData("NewCardCancelKeepDraft")]
    [InlineData("NewCardCancelDiscard")]
    public void The_new_captions_exist_in_both_languages(string key)
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var names = XDocument.Parse(Read("src", "Aiko.Pwa", "Resources", file))
                .Root!.Elements("data").Select(data => (string?)data.Attribute("name"));
            Assert.Contains(key, names);
        }
    }

    private static int Occurrences(string source, string text)
    {
        var count = 0;
        for (var at = source.IndexOf(text, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing: {signature}");
        var body = source[start..];
        var end = body.IndexOf("\n    }", StringComparison.Ordinal);
        return end < 0 ? body : body[..end];
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(NewCardDraftSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
