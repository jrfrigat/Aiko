using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Which cards hold a card back, as the board shows it: the blocking cards by name, read with the same rule the
/// start gate applies, so the board never shows a card as blocked that an agent could start, or the other way
/// round.
/// </summary>
public sealed class BoardRelationsSpecs
{
    private static readonly WorkflowDefinition Pipeline = new(
        "task",
        "Tasks",
        [
            new StageDefinition("backlog", "Backlog", 10, "Start", ["Task"], null, [], new Dictionary<string, ActionPolicy>()),
            new StageDefinition("done", "Done", 20, "Finish", ["Task"], null, [], new Dictionary<string, ActionPolicy>())
        ],
        1);

    [Fact]
    public void The_card_a_block_points_at_is_blocked_by_its_source_and_only_that_card()
    {
        var blocker = CardAt("TASK-1", "backlog");
        var blocked = CardAt("TASK-2", "backlog");
        var relations = new[] { Edge(blocker, blocked, RelationTypes.Blocks) };

        var found = Assert.Single(BoardRelations.BlockedBy(blocked, relations, [blocker, blocked], [Pipeline], []));
        Assert.Equal("TASK-1", found.CardId);

        // The source of the edge is not held back by its own block.
        Assert.Empty(BoardRelations.BlockedBy(blocker, relations, [blocker, blocked], [Pipeline], []));
    }

    [Fact]
    public void Only_a_block_blocks_and_a_card_without_one_has_no_blockers()
    {
        var one = CardAt("TASK-1", "backlog");
        var two = CardAt("TASK-2", "backlog");

        Assert.Empty(BoardRelations.BlockedBy(
            two,
            [Edge(one, two, RelationTypes.RelatesTo), Edge(one, two, RelationTypes.Implements)],
            [one, two],
            [Pipeline],
            []));
        Assert.Empty(BoardRelations.BlockedBy(two, [], [one, two], [Pipeline], []));
    }

    [Fact]
    public void A_blocker_that_finished_its_pipeline_no_longer_shows()
    {
        var blocker = CardAt("TASK-1", "done");
        var blocked = CardAt("TASK-2", "backlog");
        var relations = new[] { Edge(blocker, blocked, RelationTypes.Blocks) };

        // In its last stage but with the closing run still open: still a blocker, as the start gate says.
        Assert.Single(BoardRelations.BlockedBy(
            blocked, relations, [blocker, blocked], [Pipeline], [new StageRunSummary("TASK-1", "done", "Running")]));
        Assert.Empty(BoardRelations.BlockedBy(
            blocked, relations, [blocker, blocked], [Pipeline], [new StageRunSummary("TASK-1", "done", "Completed")]));
    }

    [Fact]
    public void The_board_row_names_its_blockers_and_the_tree_a_card_stands_in()
    {
        var section = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages", "BoardSection.razor"));

        Assert.Contains("BoardRelations.BlockedBy(", section, StringComparison.Ordinal);
        Assert.Contains("aiko-tag--warn", section, StringComparison.Ordinal);
        Assert.Contains("Loc.Format(\"BlockedByCard\"", section, StringComparison.Ordinal);

        // The card's own tree is the row under them: the number of the card it belongs to, and the count of
        // the cards that belong to it as something to press. Children are counted and never listed, so the
        // plain-link chips the row used to carry are gone.
        Assert.Contains("aiko-card__hierarchy", section, StringComparison.Ordinal);
        Assert.Contains("ParentOf(card)", section, StringComparison.Ordinal);
        Assert.Contains("ChildrenOf(card)", section, StringComparison.Ordinal);
        Assert.Contains("RelationTypes.ParentChild", section, StringComparison.Ordinal);
        Assert.Contains("class=\"aiko-card__subtasks\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("RelatedTags", section, StringComparison.Ordinal);
    }

    private static Card CardAt(string cardId, string stageId) =>
        new(
            new CardReference("project-1", cardId),
            "Task",
            $"Title of {cardId}",
            "task",
            stageId,
            1,
            0m,
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static CardRelation Edge(Card source, Card target, string type) =>
        new($"{source.Reference.CardId}-{type}-{target.Reference.CardId}", source.Reference, target.Reference, type, DateTimeOffset.UnixEpoch);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(BoardRelationsSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
