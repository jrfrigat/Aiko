using System.Xml.Linq;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// A memory document with text that the file does not have yet is not replaced behind the reader's back: a
/// click inside the page asks first, the app's own menu asks too, and a save that the daemon refused does not
/// carry the click out. Read from the markup and the code, as the other screen specs do, until the PWA has
/// component tests (TASK-216).
/// </summary>
public sealed class MemoryDraftSpecs
{
    [Fact]
    public void A_memory_document_with_unsaved_text_is_not_replaced_by_a_click()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");

        // The page is measured against what the daemon last handed over, and the mark is one property that
        // both the page's own question and the navigation ask.
        Assert.Contains("private bool IsDirty", page, StringComparison.Ordinal);
        Assert.Contains("_baseline", page, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(_content, _baseline, StringComparison.Ordinal)", page, StringComparison.Ordinal);

        // A click inside the page waits for the answer, and both ways into a document take it - a row of the
        // listing and a search result alike - as does emptying the editor for a new one.
        Assert.Contains("LeaveAsync(() => OpenDocumentAsync(path))", page, StringComparison.Ordinal);
        Assert.Contains("LeaveAsync(StartNewDocumentAsync)", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"BeginNewAsync\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnClick=\"BeginNew\"", page, StringComparison.Ordinal);

        // Leaving the page is guarded the way the card inspector and the artifact editor guard it.
        Assert.Contains("<NavigationLock", page, StringComparison.Ordinal);
        Assert.Contains("ConfirmExternalNavigation=\"@IsDirty\"", page, StringComparison.Ordinal);
        Assert.Contains("OnBeforeInternalNavigation=\"ConfirmNavigationAsync\"", page, StringComparison.Ordinal);

        var navigation = Method(page, "private async Task ConfirmNavigationAsync(LocationChangingContext context)");
        Assert.Contains("Loc.Get(\"LeaveWithUnsavedEdits\")", navigation, StringComparison.Ordinal);
        Assert.Contains("context.PreventNavigation()", navigation, StringComparison.Ordinal);

        // The reader can see that there is something to lose.
        Assert.Contains("Loc.Get(\"ProjectMemoryUnsaved\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_offers_saving_the_text_discarding_it_or_staying()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");

        Assert.Contains("<FlareDialog", page, StringComparison.Ordinal);
        Assert.Contains("<ActionsContent>", page, StringComparison.Ordinal);
        foreach (var caption in new[]
                 {
                     "ProjectMemoryLeaveTitle",
                     "ProjectMemoryLeaveHint",
                     "ProjectMemoryLeaveSave",
                     "ProjectMemoryLeaveDiscard",
                     "ProjectMemoryLeaveStay",
                 })
        {
            Assert.Contains($"Loc.Get(\"{caption}\")", page, StringComparison.Ordinal);
        }

        // Saving comes first and only a save that landed goes on; discarding lets the click through without it.
        var answer = Method(page, "private async Task AnswerLeaveAsync(bool save)");
        Assert.Contains("if (!await SaveCoreAsync())", answer, StringComparison.Ordinal);
        Assert.Contains("_baseline = _content;", answer, StringComparison.Ordinal);
        Assert.Contains("await next();", answer, StringComparison.Ordinal);

        // Staying closes the question and touches nothing.
        var stay = Method(page, "private void CancelLeave()");
        Assert.Contains("_leaveAction = null;", stay, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_save_keeps_the_text_and_says_so()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");
        var save = Method(page, "private async Task<bool> SaveCoreAsync()");

        // A refused answer and a broken connection are both answered with false, and the text counts as saved
        // only once the daemon has taken it.
        Assert.Contains("Show(await ReadErrorAsync(response), error: true);", save, StringComparison.Ordinal);
        Assert.Contains("return false;", save, StringComparison.Ordinal);
        Assert.Contains("Show(exception.Message, error: true);", save, StringComparison.Ordinal);
        Assert.Contains("_baseline = _content;", save, StringComparison.Ordinal);

        // The Save button itself is unchanged: clicking it does what it did before the guard existed.
        Assert.Contains("private async Task SaveAsync() => await SaveCoreAsync();", page, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"SaveAsync\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_project_is_another_memory()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");
        var parameters = Method(page, "protected override async Task OnParametersSetAsync()");

        Assert.Contains("_loadedProject", parameters, StringComparison.Ordinal);
        Assert.Contains("_baseline = null;", parameters, StringComparison.Ordinal);
        Assert.Contains("_content = null;", parameters, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ProjectMemoryUnsaved")]
    [InlineData("ProjectMemoryLeaveTitle")]
    [InlineData("ProjectMemoryLeaveHint")]
    [InlineData("ProjectMemoryLeaveSave")]
    [InlineData("ProjectMemoryLeaveDiscard")]
    [InlineData("ProjectMemoryLeaveStay")]
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
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(MemoryDraftSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
