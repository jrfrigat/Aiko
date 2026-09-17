using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
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
    /// <summary>Builds the default template. Settings stay null: the installation defaults fill them.</summary>
    public static ProjectTemplate Create() => new(
        ProjectTemplate.DefaultId,
        "Default",
        "The set Aiko starts from: story and task pipelines with their artifacts, two board projections "
        + "and the empty project memory.",
        ProjectTemplate.DefaultVersion,
        Settings: null,
        Workflows: [StoryWorkflow(), TaskWorkflow()],
        Projections: Projections(),
        MemoryFiles: MemoryFiles());

    private static WorkflowDefinition StoryWorkflow() =>
        new(
            "story",
            "Stories",
            [
                Stage("backlog", "Backlog", 10, CardKind.Story, "Clarify the value, the boundaries and the links of this story.", "inbox", "secondary"),
                Stage(
                    "elaboration",
                    "Elaboration",
                    20,
                    CardKind.Story,
                    "Work out the requirements and the architectural constraints of this story.",
                    "description",
                    "primary",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "The outcome of the story elaboration.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage("ready", "Ready for decomposition", 30, CardKind.Story, "Check that this story is ready to be decomposed into tasks.", "pending", "tertiary"),
                Stage("in-progress", "In progress", 40, CardKind.Story, "Coordinate the implementation of the child tasks.", "code", "warning"),
                Stage("done", "Done", 50, CardKind.Story, "Verify that the story's outcome was reached.", "done-all", "success")
            ],
            1,
            "A functional requirement large enough to be decomposed into child tasks.",
            "account-tree",
            "primary");

    private static WorkflowDefinition TaskWorkflow() =>
        new(
            "task",
            "Tasks",
            [
                Stage("backlog", "Backlog", 10, CardKind.Task, "Clarify the request, the scope and the links of this task.", "inbox", "secondary"),
                Stage(
                    "analysis",
                    "Analysis",
                    20,
                    CardKind.Task,
                    "Analyse the task, its risks and the implementation options.",
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
                    CardKind.Task,
                    "Implement the task and record the files you actually changed.",
                    "code",
                    "warning",
                    [
                        new ArtifactRequirement(
                            "implementation.md",
                            "The outcome of the implementation and its verification.",
                            MissingArtifactPolicy.Warn)
                    ]),
                Stage("review", "Review", 40, CardKind.Task, "Check the result, the tests and any deviation from the declared scope.", "check-circle", "info"),
                Stage("done", "Done", 50, CardKind.Task, "Record the outcome of this task.", "done-all", "success")
            ],
            1,
            "An atomic unit of work an agent carries out within a single stage.",
            "check-circle",
            "tertiary");

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
