using System.Text.Json;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Brings a project's workflow documents to the reserved-stage rule: every pipeline begins with
/// <c>backlog</c> and ends with <c>done</c>.
/// </summary>
/// <remarks>
/// <para>
/// The engine reads the end of a pipeline from the reserved <c>done</c> stage rather than from whatever stage
/// happens to carry the greatest order, and it refuses a document that breaks the rule both when it is written
/// and when it is read. A project authored before the rule therefore cannot simply be read - which is exactly
/// why this runs where the other upgrades of older projects run: at daemon startup, before anything reads a
/// definition, and in <c>aiko repair --fix</c>. It is idempotent: a second run finds nothing left to repair.
/// </para>
/// <para>
/// A missing <c>done</c> stage is added, because a pipeline with no end is a pipeline whose cards can never be
/// finished. A missing <c>backlog</c> stage is <em>not</em> invented: the backlog is where a card enters its
/// workflow and what its first stage says is the author's decision, so such a document is reported as not
/// repaired and named by <c>aiko doctor</c> instead.
/// </para>
/// </remarks>
public static class WorkflowStageMigrator
{
    /// <summary>
    /// The workflows of a project that break the reserved-stage rule, split into the ones a repair can bring to
    /// it and the ones it cannot, so the diagnosis can name both without writing anything.
    /// </summary>
    public static WorkflowStagePlan Plan(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var scan = Scan(projectRoot);
        return new WorkflowStagePlan(scan.Repairable, scan.Unrepairable);
    }

    /// <summary>
    /// Rewrites the workflows that still break the rule, and reports the ones it could not bring to it.
    /// </summary>
    public static WorkflowStageMigration Migrate(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var scan = Scan(projectRoot);
        var repaired = new List<string>();
        foreach (var workflow in scan.ToRepair)
        {
            var path = WorkflowPath(projectRoot, workflow.Id);
            try
            {
                WriteAtomically(path, EnsureReservedStages(workflow));
                repaired.Add(workflow.Id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be written leaves the project as readable as it was and is named in the
                // report rather than failing the run - the same answer CardLayoutMigrator gives.
                scan.Unrepairable.Add($"{workflow.Id} ({exception.GetType().Name})");
            }
        }

        return new WorkflowStageMigration(repaired, scan.Unrepairable);
    }

    /// <summary>
    /// The documents of a project that break the rule, split into the ones a repair can fix and the ones it
    /// cannot - a file that does not parse, or a pipeline with no <c>backlog</c> stage.
    /// </summary>
    private static (List<WorkflowDefinition> ToRepair, List<string> Repairable, List<string> Unrepairable) Scan(
        string projectRoot)
    {
        var toRepair = new List<WorkflowDefinition>();
        var repairable = new List<string>();
        var unrepairable = new List<string>();

        var directory = WorkflowsDirectory(projectRoot);
        if (!Directory.Exists(directory))
        {
            return (toRepair, repairable, unrepairable);
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            WorkflowDefinition workflow;
            try
            {
                using var input = File.OpenRead(path);
                workflow = JsonSerializer.Deserialize(
                    input,
                    ProjectJsonContext.Default.WorkflowDefinition)
                    ?? throw new InvalidDataException($"Invalid Aiko document: {path}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                unrepairable.Add($"{Path.GetFileNameWithoutExtension(path)} ({exception.GetType().Name})");
                continue;
            }

            if (WorkflowDefinition.RefuseReservedStages(workflow) is null)
            {
                continue;
            }

            if (!Repairable(workflow))
            {
                unrepairable.Add(workflow.Id);
                continue;
            }

            toRepair.Add(workflow);
            repairable.Add(workflow.Id);
        }

        return (toRepair, repairable, unrepairable);
    }

    /// <summary>
    /// Whether a repair can bring the workflow to the rule. A pipeline with no <c>backlog</c> stage cannot:
    /// the backlog is where a card enters, and what its first stage says belongs to the author.
    /// </summary>
    private static bool Repairable(WorkflowDefinition workflow) =>
        workflow.Stages is { Count: > 0 } &&
        workflow.Stages.Any(WorkflowDefinition.IsBacklog);

    /// <summary>
    /// The workflow with its stages in the reserved order: <c>backlog</c> first, <c>done</c> last, the rest
    /// keeping the order they had, and every stage renumbered so the pipeline has no gaps.
    /// </summary>
    private static WorkflowDefinition EnsureReservedStages(WorkflowDefinition workflow)
    {
        var stages = new List<StageDefinition>(workflow.Stages);
        if (stages.All(stage => !WorkflowDefinition.IsDone(stage)))
        {
            stages.Add(DefaultDoneStage(workflow));
        }

        var ordered = stages
            .Select((stage, index) => (Stage: stage, Index: index))
            .OrderBy(item => WorkflowDefinition.IsBacklog(item.Stage) ? 0
                : WorkflowDefinition.IsDone(item.Stage) ? 2
                : 1)
            .ThenBy(item => item.Index)
            .Select((item, index) => item.Stage with { Order = (index + 1) * 10 })
            .ToArray();

        return workflow with { Stages = ordered, Revision = workflow.Revision + 1 };
    }

    /// <summary>
    /// The <c>done</c> stage a pipeline that has none is given: it records the outcome and nothing else, so a
    /// card that reaches it is finished without the migration inventing work for the author.
    /// </summary>
    private static StageDefinition DefaultDoneStage(WorkflowDefinition workflow) =>
        new(
            WorkflowDefinition.DoneStageId,
            "Done",
            0,
            "Record the outcome of this card. Before completing the stage, re-estimate the card with "
                + "aiko_estimate_card; readiness tends to the top of its range.",
            [workflow.CardType],
            null,
            [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal),
            Icon: "done-all",
            Color: "success");

    private static string WorkflowsDirectory(string projectRoot) =>
        Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "workflows");

    private static string WorkflowPath(string projectRoot, string workflowId) =>
        PathConfinement.Resolve(
            AikoProjectPaths.DataRoot(projectRoot),
            Path.Combine(WorkflowsDirectory(projectRoot), $"{workflowId}.json"));

    /// <summary>Writes the document the way the definition store does: a temporary file, then a move.</summary>
    private static void WriteAtomically(string path, WorkflowDefinition workflow)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(output, workflow, AikoJson.Project);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

/// <summary>
/// What the workflow-stage migration did: the workflows it brought to the reserved-stage rule, and the ones it
/// could not - a file that does not parse, or a pipeline with no <c>backlog</c> stage.
/// </summary>
/// <param name="Repaired">Workflow ids that were rewritten.</param>
/// <param name="Failed">Workflows that still break the rule, named for the report.</param>
public sealed record WorkflowStageMigration(
    IReadOnlyList<string> Repaired,
    IReadOnlyList<string> Failed);

/// <summary>
/// What a project's workflows need before they satisfy the reserved-stage rule.
/// </summary>
/// <param name="Repairable">Workflows a repair can bring to the rule.</param>
/// <param name="Unrepairable">Workflows it cannot, named for the report.</param>
public sealed record WorkflowStagePlan(
    IReadOnlyList<string> Repairable,
    IReadOnlyList<string> Unrepairable);
