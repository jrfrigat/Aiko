using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;

namespace Aiko.Pwa.Services;

/// <summary>
/// The board numbers the shell and the dashboard report. Shared rather than recomputed per screen,
/// because "in progress" and "out of scope" both depend on the workflow definitions and the domain's
/// scope grammar, not on the projection a page happens to show.
/// </summary>
public static class BoardMetrics
{
    /// <summary>
    /// Whether an agent is working the card right now: it sits between the first and the last stage
    /// of its workflow. A workflow without an interior stage cannot have anything in progress.
    /// </summary>
    public static bool IsInProgress(ProjectBoardSnapshot board, Card card)
    {
        var workflow = board.Workflows.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, card.WorkflowId));
        if (workflow is null || workflow.Stages.Count < 3)
        {
            return false;
        }

        var stageIds = workflow.Stages
            .OrderBy(stage => stage.Order)
            .Select(stage => stage.Id)
            .ToArray();
        var index = Array.IndexOf(stageIds, card.StageId);
        return index > 0 && index < stageIds.Length - 1;
    }

    /// <summary>The number of cards an agent is working right now.</summary>
    public static int InProgressCount(ProjectBoardSnapshot? board) =>
        board is null ? 0 : board.Cards.Count(card => IsInProgress(board, card));

    /// <summary>The number of files reported outside every card's declared scope.</summary>
    public static int OutOfScopeCount(ProjectBoardSnapshot? board) =>
        board?.Cards.Sum(card => OutOfScopeFiles(card).Count) ?? 0;

    /// <summary>
    /// The files an agent reported that match none of the card's declared scope patterns. A card with
    /// no declared scope cannot deviate from it.
    /// </summary>
    public static IReadOnlyList<string> OutOfScopeFiles(Card card)
    {
        if (card.DeclaredScopeFiles.Count == 0)
        {
            return [];
        }

        return card.ActualChangedFiles
            .Where(file => !card.DeclaredScopeFiles.Any(pattern =>
                !string.IsNullOrWhiteSpace(pattern) &&
                !string.IsNullOrWhiteSpace(file) &&
                ScopeMatcher.Matches(pattern, file)))
            .ToArray();
    }

    /// <summary>Counts the cards of a board by type.</summary>
    public static int CountKind(ProjectBoardSnapshot? board, string kind) =>
        board?.Cards.Count(card =>
            StringComparer.OrdinalIgnoreCase.Equals(card.Kind, kind)) ?? 0;

    /// <summary>Cards whose type rolls its score up into its parents - the atomic work of the board.</summary>
    public static int BlendingCount(ProjectBoardSnapshot? board) => CountByBlending(board, blend: true);

    /// <summary>Cards whose type keeps its own score - the container or planning types of the board.</summary>
    public static int NonBlendingCount(ProjectBoardSnapshot? board) => CountByBlending(board, blend: false);

    /// <summary>
    /// Counts cards by whether their type blends with its parents. Read from the workflows, so the split is a
    /// property of the project's data rather than of a type name.
    /// </summary>
    private static int CountByBlending(ProjectBoardSnapshot? board, bool blend)
    {
        if (board is null)
        {
            return 0;
        }

        var blending = board.Workflows
            .Where(workflow => workflow.BlendsWithParent)
            .Select(workflow => workflow.Id)
            .ToHashSet(StringComparer.Ordinal);
        return board.Cards.Count(card => blending.Contains(card.WorkflowId) == blend);
    }

    /// <summary>
    /// The cards waiting in the reserved backlog stage of their workflow: the ones the backlog screen lists
    /// and nobody has taken into work yet.
    /// </summary>
    public static int BacklogCount(ProjectBoardSnapshot? board) =>
        board is null ? 0 : BacklogCount(board.Cards);

    /// <summary>
    /// The same count over a chosen set of cards.
    /// </summary>
    /// <remarks>
    /// The board's metric strip counts the backlog of the cards it is showing, so its slice agrees with the
    /// card count beside it; the rail counts the project. One rule answers both, read through <see
    /// cref="IsBacklog"/>: a second definition of "in the backlog" is how two numbers for one thing appear.
    /// </remarks>
    public static int BacklogCount(IEnumerable<Card> cards) => cards.Count(IsBacklog);

    /// <summary>Whether a card is waiting in the reserved backlog stage of its workflow.</summary>
    public static bool IsBacklog(Card card) =>
        StringComparer.Ordinal.Equals(card.StageId, WorkflowDefinition.BacklogStageId);
}
