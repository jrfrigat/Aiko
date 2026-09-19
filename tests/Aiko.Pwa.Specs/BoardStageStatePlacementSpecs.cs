using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards where a board card states the state of its current stage.
/// </summary>
/// <remarks>
/// The state used to sit in the top row beside the id and the type. TASK-47 moved it to the bottom-left
/// corner, on the same line as the size but in the other corner, so the bottom row reads "stage state on
/// the left, agents and size on the right".
/// </remarks>
public sealed class BoardStageStatePlacementSpecs
{
    [Fact]
    public void A_board_card_states_its_stage_in_the_bottom_left_corner()
    {
        var text = BoardSection();

        // The footer owns the state now ...
        var footer = text[text.IndexOf("aiko-card__dod", StringComparison.Ordinal)..];
        Assert.Contains("StageStateView.TagClass(StageStateOf(card))", footer, StringComparison.Ordinal);

        // ... and the top row no longer mentions it at all.
        var topRow = text[..text.IndexOf("aiko-card__meta", StringComparison.Ordinal)];
        Assert.DoesNotContain("StageStateView", topRow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_state_is_a_coloured_tag_with_real_text_not_only_a_tooltip()
    {
        var text = BoardSection();
        var footer = text[text.IndexOf("aiko-card__dod", StringComparison.Ordinal)..];

        // The colour is the state's own tag class ...
        Assert.Equal(1, Occurrences(footer, "StageStateView.TagClass(StageStateOf(card))"));

        // ... and the label is drawn twice: the tooltip and the visible text, so a reader gets it without
        // hovering and a screen reader gets it at all.
        Assert.Equal(2, Occurrences(footer, "StageStateView.LabelKey(StageStateOf(card))"));
    }

    [Fact]
    public void The_five_states_keep_their_distinguishable_tag_classes()
    {
        // The tag is not turned into grey text: every state still has its own class, and a state the client
        // cannot name keeps the quiet one rather than borrowing another.
        var view = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Services", "StageStateView.cs"));
        foreach (var tag in new[] { "aiko-tag--live", "aiko-tag--accent", "aiko-tag--warn", "aiko-tag--quiet" })
        {
            Assert.Contains(tag, view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_footer_wraps_so_the_state_cannot_stretch_a_narrow_column()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "css", "app.css"));
        var start = css.IndexOf(".aiko-card__meta,", StringComparison.Ordinal);
        Assert.True(start >= 0, "The card footer should keep the shared strip rule.");
        var block = css[start..css.IndexOf('}', start)];
        Assert.Contains(".aiko-card__dod", block, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap;", block, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string BoardSection() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "BoardSection.razor"));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(BoardStageStatePlacementSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
