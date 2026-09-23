using System.Xml.Linq;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// What a person types into a card or an artifact survives what happens around it: an agent writing a new
/// revision, a save refused by a conflict, a switch to another document, a click away from the page. Read from
/// the markup, as the other screen specs do, until the PWA has component tests (TASK-216).
/// </summary>
public sealed class CardDraftSpecs
{
    [Fact]
    public void A_new_revision_of_the_same_card_merges_into_the_form_instead_of_replacing_it()
    {
        var inspector = Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor");
        var parametersSet = Method(inspector, "protected override async Task OnParametersSetAsync()");

        // The card being a different card resets the form; the same card at a new revision is merged, so a
        // field the person changed keeps their text and only the untouched ones follow the card.
        Assert.Contains("_loadedCardIdentity", parametersSet, StringComparison.Ordinal);
        Assert.Contains("MergeNewRevision()", parametersSet, StringComparison.Ordinal);
        var merge = Method(inspector, "private void MergeNewRevision()");
        Assert.Contains("_baseline", merge, StringComparison.Ordinal);
        Assert.DoesNotContain("_message = null", merge, StringComparison.Ordinal);

        // The person is told, and can drop their edits for the card's.
        Assert.Contains("Loc.Get(\"CardChangedWhileEditing\")", inspector, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"DiscardMyEdits\")", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaving_a_card_with_unsaved_edits_asks_first_and_closing_the_title_drawer_puts_the_title_back()
    {
        var inspector = Read("src", "Aiko.Pwa", "Pages", "CardInspector.razor");

        Assert.Contains("<NavigationLock", inspector, StringComparison.Ordinal);
        Assert.Contains("ConfirmExternalNavigation=\"@IsDirty\"", inspector, StringComparison.Ordinal);
        var close = Method(inspector, "private Task OnEditOpenChanged(bool open)");
        Assert.Contains("_title = _baseline", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_artifact_keeps_its_own_draft_and_a_modified_one_says_so()
    {
        var editor = Read("src", "Aiko.Pwa", "Pages", "ArtifactEditor.razor");

        Assert.Contains("_drafts", editor, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ArtifactModified\")", editor, StringComparison.Ordinal);
        Assert.Contains("<NavigationLock", editor, StringComparison.Ordinal);
        // A reload over an edit asks before it throws the edit away.
        Assert.Contains("Loc.Get(\"DiscardDocumentEdits\")", editor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CardChangedWhileEditing")]
    [InlineData("DiscardMyEdits")]
    [InlineData("LeaveWithUnsavedEdits")]
    [InlineData("ArtifactModified")]
    [InlineData("DiscardDocumentEdits")]
    public void The_new_captions_exist_in_both_languages(string key)
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var names = XDocument.Parse(Read("src", "Aiko.Pwa", "Resources", file))
                .Root!.Elements("data").Select(data => (string?)data.Attribute("name"));
            Assert.Contains(key, names);
        }
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
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(CardDraftSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
