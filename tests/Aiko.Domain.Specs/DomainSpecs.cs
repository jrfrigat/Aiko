using System.Text.Json;
using Aiko.Domain.Cards;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Xunit;

namespace Aiko.Domain.Specs;

public class DomainSpecs
{
    [Fact]
    public void Task_without_parents_keeps_its_own_priority()
    {
        var result = PriorityCalculator.CalculateTask(4m, []);

        Assert.Equal(4m, result.EffectivePriority);
        Assert.Null(result.MaximumParentPriority);
    }

    [Fact]
    public void Task_uses_the_maximum_parent_priority()
    {
        var result = PriorityCalculator.CalculateTask(4m, [3m, 9m, 7m]);

        Assert.Equal(5.5m, result.EffectivePriority);
        Assert.Equal(9m, result.MaximumParentPriority);
    }

    [Fact]
    public void Cross_project_relations_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new CardRelation(
            "relation-1",
            new("project-a", "task-a"),
            new("project-b", "story-b"),
            RelationTypes.Implements,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Safe_execution_settings_serialize_managed_runs()
    {
        Assert.Equal(WorkspaceMode.Shared, ExecutionSettings.SafeDefault.WorkspaceMode);
        Assert.Equal(1, ExecutionSettings.SafeDefault.MaxConcurrentRuns);
        Assert.Equal(ActionPolicy.Deny, ExecutionSettings.SafeDefault.SharedCheckoutCommitPolicy);
    }

    [Fact]
    public void Scope_matcher_understands_recursive_globs()
    {
        Assert.True(ScopeMatcher.Matches("src/**", "src/Aiko.Server/Program.cs"));
        Assert.True(ScopeMatcher.Matches("tests/*/Program.cs", "tests/Specs/Program.cs"));
        Assert.False(ScopeMatcher.Matches("src/**", "docs/specification.md"));
    }

    [Fact]
    public void Scope_matcher_detects_pattern_overlaps()
    {
        Assert.True(ScopeMatcher.Overlaps("src/**", "src/Aiko.Server/**"));
        Assert.True(ScopeMatcher.Overlaps("tests/*/Program.cs", "tests/Specs/Program.cs"));
        Assert.True(ScopeMatcher.Overlaps("**", "src/**"));
        Assert.False(ScopeMatcher.Overlaps("src/**", "tests/**"));
        Assert.False(ScopeMatcher.Overlaps("a/*/c.cs", "a/b/d.cs"));
    }

    [Fact]
    public void Priority_criteria_normalize_values_by_their_range_and_weight_them()
    {
        PriorityCriterion[] criteria =
        [
            new("readiness", "Готовность", "Насколько задача готова к взятию", 1m, 0m, 10m),
            new("size", "Размер", "Оценка объёма работы", 2m, 0m, 10m),
            new("benefit", "Польза", "Польза для продукта", 3m, 0m, 10m)
        ];
        var values = new Dictionary<string, decimal>
        {
            ["readiness"] = 4m,
            ["size"] = 5m,
            ["benefit"] = 10m
        };

        // ((4/10)*1 + (5/10)*2 + (10/10)*3) / (1+2+3)
        var priority = PriorityCalculator.CalculateOwnPriority(values, criteria);

        Assert.Equal(4.4m / 6m, priority);
    }

    [Fact]
    public void Values_outside_a_criterion_range_are_clamped()
    {
        PriorityCriterion[] criteria = [new("benefit", "Польза", "Польза для продукта", 1m, 0m, 10m)];

        Assert.Equal(1m, PriorityCalculator.CalculateOwnPriority(
            new Dictionary<string, decimal> { ["benefit"] = 40m },
            criteria));
        Assert.Equal(0m, PriorityCalculator.CalculateOwnPriority(
            new Dictionary<string, decimal> { ["benefit"] = -5m },
            criteria));
    }

    [Fact]
    public void A_criterion_with_an_empty_range_scores_nothing_instead_of_dividing_by_zero()
    {
        PriorityCriterion[] criteria = [new("broken", "Сломанный", "Диапазон задан неверно", 1m, 5m, 5m)];

        var priority = PriorityCalculator.CalculateOwnPriority(
            new Dictionary<string, decimal> { ["broken"] = 5m },
            criteria);

        Assert.Equal(0m, priority);
    }

    [Fact]
    public void The_card_size_multiplies_the_own_score()
    {
        var settings = new PrioritySettings(
            PriorityWeights.Default,
            [],
            [
                new("S", "S", "До половины дня", 1.08m),
                new("M", "M", "До дня работы", 1m),
                new("XL", "XL", "Больше недели", 0.8m)
            ]);

        Assert.Equal(8m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "M"));
        Assert.Equal(8.64m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "S"));
        Assert.Equal(6.4m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "XL"));
        // No size, and a step that is not in the grid any more, are both neutral.
        Assert.Equal(8m, PriorityCalculator.CalculateOwnScore(8m, null, settings, null));
        Assert.Equal(8m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "XXXL"));
    }

    [Fact]
    public void Criterion_values_win_over_the_manual_priority_only_when_the_project_has_criteria()
    {
        var settings = new PrioritySettings(
            PriorityWeights.Default,
            [new("benefit", "Польза", "Польза для продукта", 1m, 0m, 10m)]);
        var values = new Dictionary<string, decimal> { ["benefit"] = 5m };

        // Criteria configured and a value present: the value decides.
        Assert.Equal(0.5m, PriorityCalculator.CalculateOwnScore(9m, values, settings, null));
        // No value on the card: the manually entered priority stands.
        Assert.Equal(9m, PriorityCalculator.CalculateOwnScore(9m, null, settings, null));
        // No criteria in the project at all: likewise.
        Assert.Equal(
            9m,
            PriorityCalculator.CalculateOwnScore(9m, values, PrioritySettings.SafeDefault, null));
    }

    [Fact]
    public void The_safe_default_carries_the_standard_size_grid()
    {
        // Static initializers run in declaration order, so a default built above the grid would capture a
        // null one and every card would silently score without a coefficient.
        Assert.NotEmpty(PrioritySettings.DefaultGrid);
        Assert.Equal(PrioritySettings.DefaultGrid.Count, PrioritySettings.SafeDefault.Grid.Count);
        Assert.Equal(1m, PrioritySettings.SafeDefault.SizeFactor("M"));
        Assert.True(PrioritySettings.SafeDefault.SizeFactor("XL") < 1m);
        Assert.True(PrioritySettings.SafeDefault.SizeFactor("XS") > 1m);
    }

    [Fact]
    public void A_stage_allows_its_default_adapter_whatever_the_executor_list_says()
    {
        var stage = new StageDefinition(
            "analysis",
            "Analysis",
            20,
            "Analyse the task.",
            [CardKind.Task],
            "claude",
            [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal),
            ["codex"]);

        // The default adapter is always allowed, so a stage cannot be configured into a dead end.
        Assert.True(stage.Allows("claude"));
        Assert.True(stage.Allows("codex"));
        Assert.False(stage.Allows("cursor"));
        // No adapter named means "any", which is how an unpinned run is started.
        Assert.True(stage.Allows(null));

        // An empty executor list means every discovered adapter is allowed.
        Assert.True((stage with { AllowedAgentAdapterIds = null }).Allows("cursor"));

        // With no default, the list is the only thing that decides.
        Assert.False((stage with { DefaultAgentAdapterId = null }).Allows("cursor"));
    }

    [Fact]
    public void Stage_executors_and_validation_commands_survive_the_workflow_json()
    {
        var stage = new StageDefinition(
            "implementation",
            "Implementation",
            30,
            "Implement the task.",
            [CardKind.Task],
            "claude",
            [new ArtifactRequirement("implementation.md", "The outcome.", MissingArtifactPolicy.Warn)],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal) { ["commit"] = ActionPolicy.Ask },
            ["claude", "codex"],
            ["dotnet build --no-restore", "dotnet test --no-build"],
            ["aiko-memory"],
            ["aiko-report"]);

        var json = JsonSerializer.Serialize(stage, JsonSerializerOptions.Web);
        var restored = JsonSerializer.Deserialize<StageDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(restored);
        Assert.Equal(stage.Id, restored!.Id);
        Assert.Equal(stage.Instruction, restored.Instruction);
        Assert.Equal("claude", restored.DefaultAgentAdapterId);
        Assert.Equal(["claude", "codex"], restored.AllowedAgents);
        Assert.Equal(["dotnet build --no-restore", "dotnet test --no-build"], restored.Commands);
        Assert.Equal(["aiko-memory"], restored.BeforeSkills);
        Assert.Equal(["aiko-report"], restored.AfterSkills);
        Assert.Equal(ActionPolicy.Ask, restored.ActionPolicies["commit"]);
        Assert.Equal(MissingArtifactPolicy.Warn, restored.RequiredArtifacts[0].MissingPolicy);
    }

    [Fact]
    public void A_size_step_must_carry_an_id_and_a_positive_coefficient()
    {
        Assert.Throws<ArgumentException>(() => new SizeDefinition(" ", "S", "Описание", 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeDefinition("S", "S", "Описание", 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeDefinition("S", "S", "Описание", -1m));
    }

    [Fact]
    public void Domain_enums_round_trip_with_web_json_defaults()
    {
        // A card type is a plain string now - the type is the workflow a card moves through, and a project
        // may add its own - so what matters is that the id survives JSON unchanged, not the casing rule of a
        // converter. MissingArtifactPolicy is still an enum and still goes through the string converter.
        var kindJson = JsonSerializer.Serialize(CardKind.Task, JsonSerializerOptions.Web);
        Assert.Equal("\"Task\"", kindJson);
        Assert.Equal(CardKind.Task, JsonSerializer.Deserialize<string>(kindJson, JsonSerializerOptions.Web));

        var policyJson = JsonSerializer.Serialize(MissingArtifactPolicy.Warn, JsonSerializerOptions.Web);
        Assert.Equal("\"Warn\"", policyJson);
        Assert.Equal(
            MissingArtifactPolicy.Warn,
            JsonSerializer.Deserialize<MissingArtifactPolicy>(policyJson, JsonSerializerOptions.Web));
    }

    [Fact]
    public void A_card_type_is_named_after_the_workflow_that_defines_it()
    {
        Assert.Equal("Story", CardKind.FromWorkflowId("story"));
        Assert.Equal("Task", CardKind.FromWorkflowId("task"));
        Assert.Equal("Epic", CardKind.FromWorkflowId("epic"));
        Assert.Equal("story", CardKind.ToWorkflowId("Story"));
        Assert.Equal("epic", CardKind.ToWorkflowId("Epic"));

        // A document may spell a built-in type either way; the canonical form is the one the template uses,
        // and any other id is title-cased so one type never compares unequal to itself within a project.
        Assert.Equal(CardKind.Story, CardKind.Canonical("story"));
        Assert.Equal(CardKind.Task, CardKind.Canonical("TASK"));
        Assert.Equal("Epic", CardKind.Canonical("epic"));
    }

    [Fact]
    public void The_appearance_catalog_rejects_names_it_does_not_know()
    {
        Assert.Equal(10, AppearanceCatalog.Icons.Count);

        Assert.True(AppearanceCatalog.IsValidIcon("inbox"));
        // No choice is a valid state: every stage and type written before the option existed has none.
        Assert.True(AppearanceCatalog.IsValidIcon(null));
        Assert.False(AppearanceCatalog.IsValidIcon("rocket"));

        Assert.True(AppearanceCatalog.IsValidColor("secondary"));
        Assert.True(AppearanceCatalog.IsValidColor(null));
        Assert.False(AppearanceCatalog.IsValidColor("chartreuse"));

        Assert.Null(AppearanceCatalog.NormalizeIcon(" "));
        Assert.Equal("target", AppearanceCatalog.NormalizeIcon(" target "));
    }
}
