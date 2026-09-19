using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// The built-in default template: the set Aiko is ready to work with right after installation.
/// </summary>
/// <remarks>
/// These workflows, projections and memory files used to be hardcoded inside
/// <see cref="ProjectInitializer"/>, which made the initializer the only source of "what a project starts
/// as" - and left the global settings screen unable to say anything about stages. They live here now, are
/// written to the templates root on first use, and are copied into a project at init like any other
/// template's content. Editing the file below the templates root changes what new projects start from; it
/// never reaches a project that already took its copy.
/// </remarks>
internal static class BuiltInProjectTemplate
{
    /// <summary>Builds the default template, fully stated: settings, pipelines, projections and memory.</summary>
    public static ProjectTemplate Create() => new(
        ProjectTemplate.DefaultId,
        "Default",
        "The set Aiko starts from: epic, story and task pipelines with their statuses, artifacts and per-status "
        + "instructions, the app point / user point / complete scoring criteria, the epics / stories / tasks board "
        + "projections and the empty project memory.",
        ProjectTemplate.DefaultVersion,
        Settings: new AppSettings(
            AppSettings.CurrentSchemaVersion,
            ExecutionSettings.SafeDefault,
            PrioritySettings.Standard),
        Workflows: [EpicWorkflow(), StoryWorkflow(), TaskWorkflow()],
        Projections: Projections(),
        MemoryFiles: MemoryFiles());

    /// <summary>
    /// The epic pipeline: a goal held across other cards rather than a unit of work of its own.
    /// </summary>
    /// <remarks>
    /// Three stages on purpose. An epic is a container - the work happens in its child stories and tasks, and
    /// decomposition belongs to the story pipeline (<c>elaboration</c> then <c>ready</c>), so a stage that
    /// "splits" an epic would be a second place where the same planning happens. The instructions therefore say
    /// what the container does at each step instead of describing work of its own.
    /// </remarks>
    private static WorkflowDefinition EpicWorkflow() =>
        new(
            "epic",
            "Epics",
            [
                Stage("backlog", "Backlog", 10, "Epic", "Clarify what the epic is for, where its boundary is and which stories or tasks carry it, and state that on the card. An epic holds a goal together rather than being a unit of work: what fits in a single stage is a story. Re-estimate the card with aiko_estimate_card when the picture changes.", "inbox", "secondary"),
                Stage("in-progress", "In progress", 20, "Epic", "Coordinate the child stories and tasks: keep the epic's scope honest, move the work into them instead of doing it on the epic itself, and re-estimate the card with aiko_estimate_card after every change so its score says what the epic now holds.", "code", "warning"),
                Stage("done", "Done", 30, "Epic", "Verify that the epic's outcome was reached - its child stories are finished or deliberately dropped - and record the result. Before completing the stage, re-estimate the card with aiko_estimate_card; readiness tends to the top of its range.", "done-all", "success")
            ],
            1,
            "A goal held across several stories or tasks. An epic is not implemented in one stage: it is the "
            + "container the work of others belongs to, its score says how much of that outcome exists, and it is "
            + "finished when its children are.",
            "layers",
            "info");

    private static WorkflowDefinition StoryWorkflow() =>
        new(
            "story",
            "Stories",
            [
                Stage("backlog", "Backlog", 10, "Story", "Clarify the value, the boundaries and the links of this story, and state its requirements on the card. Re-estimate the card with aiko_estimate_card when the picture changes.", "inbox", "secondary"),
                Stage(
                    "elaboration",
                    "Elaboration",
                    20,
                    "Story",
                    "Work out the requirements and the architectural constraints of this story and record them in analysis.md. Before completing the stage, re-estimate the card with aiko_estimate_card - app-point and readiness - with what you learned.",
                    "description",
                    "primary",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "The outcome of the story elaboration.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage("ready", "Ready for decomposition", 30, "Story", "Check that the requirements are unambiguous and that the story can be split into tasks. Before completing the stage, re-estimate the card with aiko_estimate_card; readiness must match the story as it is now.", "pending", "tertiary"),
                Stage("in-progress", "In progress", 40, "Story", "Coordinate the implementation of the child tasks. After every change, re-estimate the card with aiko_estimate_card so its score says what the story now does.", "code", "warning"),
                Stage("done", "Done", 50, "Story", "Verify that the story's outcome was reached and summarize it. Before completing the stage, re-estimate the card with aiko_estimate_card; readiness tends to the top of its range.", "done-all", "success")
            ],
            1,
            "A functional requirement or a user-facing capability, large enough to be decomposed into tasks. "
            + "Its app-point and user-point say why it matters; its complete score is re-scored after every "
            + "piece of work.",
            "account-tree",
            "primary");

    private static WorkflowDefinition TaskWorkflow() =>
        new(
            "task",
            "Tasks",
            [
                Stage("backlog", "Backlog", 10, "Task", "Clarify the request, the scope and the links of this task, and state its requirements on the card. Re-estimate the card with aiko_estimate_card when they change.", "inbox", "secondary"),
                Stage(
                    "analysis",
                    "Analysis",
                    20,
                    "Task",
                    "Analyse the task, its risks and the implementation options and record them in analysis.md. Before completing the stage, re-estimate the card with aiko_estimate_card - app-point and readiness - with what you learned.",
                    "description",
                    "primary",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "The outcome of the task analysis.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage(
                    "implementation",
                    "Implementation",
                    30,
                    "Task",
                    "Implement the task and record the files you actually changed. Before completing the stage, re-estimate the card with aiko_estimate_card: readiness must describe the card as it is after the change, and app-point only changes when the work altered the technical debt.",
                    "code",
                    "warning",
                    [
                        new ArtifactRequirement(
                            "implementation.md",
                            "The outcome of the implementation and its verification.",
                            MissingArtifactPolicy.Warn)
                    ]),
                Stage("review", "Review", 40, "Task", "Check the result, the tests and any deviation from the declared scope. Before completing the stage, re-estimate the card with aiko_estimate_card with what is left to do.", "check-circle", "info"),
                Stage("done", "Done", 50, "Task", "Record the outcome of this task. Before completing the stage, re-estimate the card with aiko_estimate_card; readiness tends to the top of its range.", "done-all", "success")
            ],
            1,
            "An atomic unit of work an agent carries out within a single stage: one change, one verification. "
            + "Its complete score is recalculated after each run, so the board shows what is actually done.",
            "check-circle",
            "tertiary",
            BlendsWithParent: true);

    private static StageDefinition Stage(
        string id,
        string title,
        int order,
        string kind,
        string instruction,
        string icon,
        string color,
        IReadOnlyList<ArtifactRequirement>? artifacts = null) =>
        new(
            id,
            title,
            order,
            instruction,
            [kind],
            null,
            artifacts ?? [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal),
            Icon: icon,
            Color: color);

    private static IReadOnlyList<BoardProjectionDefinition> Projections() =>
    [
        new(1, "tasks", "Tasks", "kanban", "task", "stage", EmptyFilters()),
        new(1, "stories", "Stories", "kanban", "story", "stage", EmptyFilters()),
        new(1, "epics", "Epics", "kanban", "epic", "stage", EmptyFilters()),
        new(1, "combined", "Combined", "swimlane", null, "story", EmptyFilters())
    ];

    private static IReadOnlyList<TemplateDocument> MemoryFiles() =>
    [
        new("memory/index.md", "# Project memory\n\nThis index links the durable knowledge of the project.\n"),
        new("memory/architecture.md", "# Architecture\n\n"),
        new("memory/conventions.md", "# Conventions\n\n"),
        new("memory/lessons.md", "# Lessons learned\n\n")
    ];

    private static IReadOnlyDictionary<string, string> EmptyFilters() =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
