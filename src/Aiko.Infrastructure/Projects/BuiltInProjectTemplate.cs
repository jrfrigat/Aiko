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
                Stage("backlog", "Бэклог", 10, CardKind.Story, "Уточни ценность, границы и связи story."),
                Stage(
                    "elaboration",
                    "Проработка",
                    20,
                    CardKind.Story,
                    "Проработай требования и архитектурные ограничения story.",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "Результат проработки story.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage("ready", "Готово к декомпозиции", 30, CardKind.Story, "Проверь готовность story к декомпозиции."),
                Stage("in-progress", "В работе", 40, CardKind.Story, "Координируй реализацию дочерних задач."),
                Stage("done", "Завершено", 50, CardKind.Story, "Проверь достижение результата story.")
            ],
            1);

    private static WorkflowDefinition TaskWorkflow() =>
        new(
            "task",
            "Tasks",
            [
                Stage("backlog", "Бэклог", 10, CardKind.Task, "Уточни запрос, scope и связи задачи."),
                Stage(
                    "analysis",
                    "Анализ",
                    20,
                    CardKind.Task,
                    "Проанализируй задачу, риски и варианты реализации.",
                    [
                        new ArtifactRequirement(
                            "analysis.md",
                            "Результат анализа задачи.",
                            MissingArtifactPolicy.NeedsAttention)
                    ]),
                Stage(
                    "implementation",
                    "Реализация",
                    30,
                    CardKind.Task,
                    "Реализуй задачу и зафиксируй фактически измененные файлы.",
                    [
                        new ArtifactRequirement(
                            "implementation.md",
                            "Итог реализации и проверки.",
                            MissingArtifactPolicy.Warn)
                    ]),
                Stage("review", "Проверка", 40, CardKind.Task, "Проверь результат, тесты и отклонения от scope."),
                Stage("done", "Завершено", 50, CardKind.Task, "Зафиксируй итог выполнения задачи.")
            ],
            1);

    private static StageDefinition Stage(
        string id,
        string title,
        int order,
        CardKind kind,
        string instruction,
        IReadOnlyList<ArtifactRequirement>? artifacts = null) =>
        new(
            id,
            title,
            order,
            instruction,
            [kind],
            null,
            artifacts ?? [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal));

    private static IReadOnlyList<BoardProjectionDefinition> Projections() =>
    [
        new(1, "tasks", "Tasks", "kanban", "task", "stage", EmptyFilters()),
        new(1, "stories", "Stories", "kanban", "story", "stage", EmptyFilters()),
        new(1, "combined", "Combined", "swimlane", null, "story", EmptyFilters())
    ];

    private static IReadOnlyList<TemplateDocument> MemoryFiles() =>
    [
        new("memory/index.md", "# Память проекта\n\nЭтот индекс содержит ссылки на устойчивые знания проекта.\n"),
        new("memory/architecture.md", "# Архитектура\n\n"),
        new("memory/conventions.md", "# Соглашения\n\n"),
        new("memory/lessons.md", "# Накопленный опыт\n\n")
    ];

    private static IReadOnlyDictionary<string, string> EmptyFilters() =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
