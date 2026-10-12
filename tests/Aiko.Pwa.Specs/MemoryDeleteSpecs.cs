using System.Xml.Linq;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Deleting a document from the project's memory is a question with the document's name in it, and the safe
/// answer is the one that comes first: the file is gone for good, and nothing on the screen brings it back.
/// Read from the markup and the code, as the other screen specs do, until the PWA has component tests
/// (TASK-216).
/// </summary>
public sealed class MemoryDeleteSpecs
{
    [Fact]
    public void Deleting_a_memory_document_asks_first_and_names_the_document()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");

        // One click raises the question instead of sending the delete.
        Assert.Contains("OnClick=\"AskDelete\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnClick=\"DeleteAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("private bool _deleteAsk", page, StringComparison.Ordinal);

        // The question names the file that would go: the path the editor holds, not one remembered by a list.
        Assert.Contains("<FlareDialog Visible=\"@_deleteAsk\"", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ProjectMemoryDeleteTitle\")", page, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"ProjectMemoryDeleteWarning\")", page, StringComparison.Ordinal);
        Assert.Contains("@_open?.Path", page, StringComparison.Ordinal);

        // Nothing is asked about when no document is open.
        var ask = Method(page, "private void AskDelete()");
        Assert.Contains("_open?.Path is { Length: > 0 }", ask, StringComparison.Ordinal);
    }

    [Fact]
    public void The_safe_answer_comes_first_and_closing_the_question_sends_nothing()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");

        // Escape and a click on the scrim close the dialog, and closing it is keeping the document.
        Assert.Contains("VisibleChanged=\"value => _deleteAsk = value\"", page, StringComparison.Ordinal);
        Assert.Contains("private void CancelDelete() => _deleteAsk = false;", page, StringComparison.Ordinal);

        // Keeping is the first action in the dialog and the destructive one the second.
        var actions = page.IndexOf("<ActionsContent>", StringComparison.Ordinal);
        var keep = page.IndexOf("Loc.Get(\"ProjectMemoryDeleteKeep\")", StringComparison.Ordinal);
        var confirm = page.IndexOf("Loc.Get(\"ProjectMemoryDeleteConfirm\")", StringComparison.Ordinal);
        Assert.True(keep > actions && confirm > keep, "the safe answer comes before the delete");

        // The answer that deletes does exactly what the button used to do, in one place.
        var confirmMethod = Method(page, "private async Task ConfirmDeleteAsync()");
        Assert.Contains("_deleteAsk = false;", confirmMethod, StringComparison.Ordinal);
        Assert.Contains("await DeleteAsync();", confirmMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_named_document_is_deleted_and_a_refusal_leaves_the_editor_alone()
    {
        var page = Read("src", "Aiko.Pwa", "Pages", "MemoryPage.razor");
        var delete = Method(page, "private async Task DeleteAsync()");

        // The path in the request is the open document's, and the daemon's own sentence is what a refusal says.
        Assert.Contains("$\"{BaseUrl}/document?path={Uri.EscapeDataString(path)}\"", delete, StringComparison.Ordinal);
        Assert.Contains("Show(await ReadErrorAsync(response), error: true);", delete, StringComparison.Ordinal);
        Assert.Contains("Show(exception.Message, error: true);", delete, StringComparison.Ordinal);

        // The editor is emptied only after the delete has landed: a refusal returns before that, with the
        // document still open in front of the reader.
        var refusal = delete.IndexOf("Show(await ReadErrorAsync(response), error: true);", StringComparison.Ordinal);
        var cleared = delete.IndexOf("_open = null;", StringComparison.Ordinal);
        Assert.True(refusal >= 0 && cleared > refusal, "the editor is emptied only after the delete succeeded");
    }

    [Theory]
    [InlineData("ProjectMemoryDeleteTitle")]
    [InlineData("ProjectMemoryDeleteWarning")]
    [InlineData("ProjectMemoryDeleteKeep")]
    [InlineData("ProjectMemoryDeleteConfirm")]
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
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(MemoryDeleteSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
