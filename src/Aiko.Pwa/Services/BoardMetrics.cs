using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;

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

    /// <summary>
    /// Whether the project runs several agents at once in one checkout. That combination is the one the
    /// board warns about: parallel runs in a shared workspace can edit the same files, and the warning is
    /// the only place the risk is stated before it happens.
    /// </summary>
    /// <param name="settings">The project's effective execution settings, or null when unresolved.</param>
    public static bool IsParallelShared(ExecutionSettings? settings) =>
        settings is not null &&
        settings.WorkspaceMode == WorkspaceMode.Shared &&
        settings.MaxConcurrentRuns > 1;

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

    /// <summary>Counts the cards of a board by kind.</summary>
    public static int CountKind(ProjectBoardSnapshot? board, CardKind kind) =>
        board?.Cards.Count(card => card.Kind == kind) ?? 0;
}
