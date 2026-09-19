using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the live-update wiring of the screens that draw the open project's board.
/// </summary>
/// <remarks>
/// The bug this pins: an SSE event reloads the board in <c>WorkspaceState</c> and raises <c>Changed</c>, but a
/// page that reads <c>State.Board</c> in its markup and never subscribes keeps rendering the snapshot it was
/// built with. The board and the card page then stay stale until a manual reload while the shell's badge says
/// "SSE LIVE" - which is exactly how TASK-7 was closed as done without the live update working.
/// </remarks>
public sealed class LiveBoardSubscriptionSpecs
{
    [Fact]
    public void A_page_that_draws_the_live_board_re_renders_when_the_workspace_changes()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages"),
                     "*.razor",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            // Only a reference in the markup is a drawn snapshot of the board. One inside the code block may be
            // a one-off default (a template id, a form's starting type) that no live change has to repaint.
            var codeIndex = text.IndexOf("@code", StringComparison.Ordinal);
            var markup = codeIndex < 0 ? text : text[..codeIndex];
            if (!markup.Contains("State.Board", StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.Contains("State.Changed += OnStateChanged;", StringComparison.Ordinal) ||
                !text.Contains("State.Changed -= OnStateChanged;", StringComparison.Ordinal) ||
                !text.Contains("@implements IAsyncDisposable", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A page that draws State.Board must subscribe to State.Changed in OnInitialized and unsubscribe in "
            + "DisposeAsync, or it will not re-render after a live update. Missing on:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_board_and_the_card_page_are_covered()
    {
        // The two screens the owner reported stale: whatever else changes, these must keep following the board.
        var pages = Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages");
        foreach (var page in new[] { "BoardView.razor", "CardPage.razor" })
        {
            var text = File.ReadAllText(Path.Combine(pages, page));
            Assert.Contains("State.Changed += OnStateChanged;", text, StringComparison.Ordinal);
            Assert.Contains("State.Changed -= OnStateChanged;", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(LiveBoardSubscriptionSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
