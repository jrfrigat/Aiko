using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the feedback the memory screen gives when it opens a document. Everything mechanical about that
/// button was verified as working - the handler is awaited, the request answers, the editor's textarea takes
/// its value as a DOM property - and the page still read as "the button does nothing", because the editor sits
/// below a list that is longer than the screen and the screen said nothing at all. What is pinned here is the
/// answer to that: the screen reports the open, marks the row where it was opened, and brings the reader to the
/// document.
/// </summary>
public sealed class MemoryOpenFeedbackSpecs
{
    [Fact]
    public void Opening_a_document_reports_itself_like_saving_and_deleting_do()
    {
        var page = Page();

        // The third action of the screen tells the reader what happened; the other two already did.
        Assert.Contains("Loc.Format(\"ProjectMemoryOpened\"", page, StringComparison.Ordinal);
        Assert.Contains("Show(Loc.Format(\"ProjectMemoryOpened\"", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ProjectMemorySaved\"", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"ProjectMemoryDeleted\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_open_document_is_marked_where_it_was_opened()
    {
        var page = Page();

        // One definition and two uses: the listing and the search results both mark the open document, so the
        // click leaves a trace in the part of the page the reader is looking at.
        Assert.Equal(3, Regex.Matches(page, @"IsOpenPath\(").Count);
        Assert.Contains("private bool IsOpenPath(string path)", page, StringComparison.Ordinal);
        Assert.Contains("@if (IsOpenPath(result.Path))", page, StringComparison.Ordinal);
        Assert.Contains("@if (IsOpenPath(item.Path))", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reader_is_brought_to_the_editor_after_it_was_redrawn()
    {
        var page = Page();

        // The anchor is where the jump lands, the render hook is when it happens, and the call is the browser's.
        Assert.Contains("<div @ref=\"_editor\"></div>", page, StringComparison.Ordinal);
        Assert.Contains("protected override async Task OnAfterRenderAsync(bool firstRender)", page, StringComparison.Ordinal);
        Assert.Contains("await Js.InvokeVoidAsync(\"aikoScrollIntoView\", _editor);", page, StringComparison.Ordinal);

        // A scroll the browser cannot do costs the scroll and nothing else: the document is open either way.
        Assert.Contains("catch (JSException)", page, StringComparison.Ordinal);

        var script = Read("src", "Aiko.Pwa", "wwwroot", "js", "aiko-events.js");
        Assert.Contains("window.aikoScrollIntoView = function (element)", script, StringComparison.Ordinal);
        Assert.Contains("element.scrollIntoView(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_texts_are_carried_by_both_languages()
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var text = Read("src", "Aiko.Pwa", "Resources", file);

            Assert.Contains("name=\"ProjectMemoryOpened\"", text, StringComparison.Ordinal);
            Assert.Contains("name=\"ProjectMemoryOpenNow\"", text, StringComparison.Ordinal);
        }
    }

    private static string Page() => Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. path]));

    /// <summary>Walks up from this assembly to the solution file, the way the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(MemoryOpenFeedbackSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
