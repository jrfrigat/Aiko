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
    public void Priority_criteria_compute_weighted_own_priority()
    {
        PriorityCriterion[] criteria =
        [
            new("readiness", "Готовность", "Насколько задача готова к взятию", 1m),
            new("size", "Размер", "Оценка объёма работы", 2m),
            new("benefit", "Польза", "Польза для продукта", 3m)
        ];
        var values = new Dictionary<string, decimal>
        {
            ["readiness"] = 4m,
            ["size"] = 5m,
            ["benefit"] = 10m
        };

        // (4*1 + 5*2 + 10*3) / (1+2+3) = 44 / 6
        var priority = PriorityCalculator.CalculateOwnPriority(values, criteria);

        Assert.Equal(44m / 6m, priority);
    }

    [Fact]
    public void Domain_enums_round_trip_with_web_json_defaults()
    {
        var kindJson = JsonSerializer.Serialize(CardKind.Task, JsonSerializerOptions.Web);
        Assert.Equal("\"Task\"", kindJson);
        Assert.Equal(CardKind.Task, JsonSerializer.Deserialize<CardKind>(kindJson, JsonSerializerOptions.Web));

        var policyJson = JsonSerializer.Serialize(MissingArtifactPolicy.Warn, JsonSerializerOptions.Web);
        Assert.Equal("\"Warn\"", policyJson);
        Assert.Equal(
            MissingArtifactPolicy.Warn,
            JsonSerializer.Deserialize<MissingArtifactPolicy>(policyJson, JsonSerializerOptions.Web));
    }
}
