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
        // The same answer for push: a project that never stated one does not push from the shared checkout.
        Assert.Equal(ActionPolicy.Deny, ExecutionSettings.SafeDefault.SharedCheckoutPushPolicy);

        // The push policy is optional in the constructor, and that is what makes a settings document written
        // before it existed readable: a missing field has to land on the safe value rather than fail the load.
        Assert.Equal(
            ActionPolicy.Deny,
            new ExecutionSettings(
                WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Allow).SharedCheckoutPushPolicy);

        // The scope-expansion policy is optional for the same reason, and its safe value is Ask: a document
        // written before it existed reads as the behaviour Aiko had then - a question - and not as permission.
        Assert.Equal(ActionPolicy.Ask, ExecutionSettings.SafeDefault.ScopeExpansionPolicy);
        Assert.Equal(
            ActionPolicy.Ask,
            new ExecutionSettings(
                WorkspaceMode.Shared, 1, ActionPolicy.Ask, ActionPolicy.Deny, ActionPolicy.Deny)
                .ScopeExpansionPolicy);
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
    public void The_standard_criteria_are_shares_of_one_score_on_a_0_to_10_scale()
    {
        var criteria = PrioritySettings.StandardCriteria;

        Assert.Equal(["app-point", "user-point", "complete"], criteria.Select(item => item.Id).ToArray());
        Assert.Equal(1m, criteria.Sum(item => item.Weight));
        Assert.All(criteria, item =>
        {
            Assert.Equal(0m, item.Minimum);
            Assert.Equal(10m, item.Maximum);
            Assert.False(string.IsNullOrWhiteSpace(item.AiInstruction));
        });

        // Readiness has to be there by the id the pipelines and /aiko-run name when they ask the agent to
        // re-score it after every change.
        Assert.Contains(criteria, item => item.Id == "complete");

        // The safe default stays criteria-less, so an upgrade never quietly hands an existing project a
        // scoring model it did not opt into.
        Assert.Empty(PrioritySettings.SafeDefault.Criteria);
        Assert.NotNull(PrioritySettings.Standard.Sizes);
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

        // A manual priority is on the board's 0..100 scale, so 8 is a score of 0.08.
        Assert.Equal(0.08m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "M"));
        Assert.Equal(0.0864m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "S"));
        Assert.Equal(0.064m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "XL"));
        // No size, and a step that is not in the grid any more, are both neutral.
        Assert.Equal(0.08m, PriorityCalculator.CalculateOwnScore(8m, null, settings, null));
        Assert.Equal(0.08m, PriorityCalculator.CalculateOwnScore(8m, null, settings, "XXXL"));
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
        // No value on the card: the manually entered priority stands, on the same 0..1 scale as a score - an
        // unscored card with a manual 9 no longer outranks every scored one.
        Assert.Equal(0.09m, PriorityCalculator.CalculateOwnScore(9m, null, settings, null));
        // No criteria in the project at all: likewise.
        Assert.Equal(
            0.09m,
            PriorityCalculator.CalculateOwnScore(9m, values, PrioritySettings.SafeDefault, null));
    }

    [Fact]
    public void A_bad_value_read_from_disk_is_clamped_rather_than_taking_the_board_down()
    {
        var settings = PrioritySettings.SafeDefault;

        // A negative manual priority scores nothing, and one above the scale is the top of it.
        Assert.Equal(0m, PriorityCalculator.CalculateOwnScore(-1m, null, settings, null));
        Assert.Equal(1m, PriorityCalculator.CalculateOwnScore(250m, null, settings, null));

        // A negative own score or parent is read as zero.
        var task = PriorityCalculator.CalculateTask(-3m, [-1m, 0.4m]);
        Assert.Equal(0m, task.OwnPriority);
        Assert.Equal(0.4m, task.MaximumParentPriority);

        // A criterion with a negative weight does not count.
        var score = PriorityCalculator.CalculateOwnPriority(
            new Dictionary<string, decimal> { ["good"] = 10m, ["bad"] = 10m },
            [new("good", "Good", "", 1m, 0m, 10m), new("bad", "Bad", "", -5m, 0m, 10m)]);
        Assert.Equal(1m, score);
    }

    [Theory]
    [InlineData(-0.1, 0, 10, "weight")]
    [InlineData(1, 5, 5, "range")]
    [InlineData(1, 10, 0, "range")]
    public void Settings_with_an_impossible_criterion_are_refused_with_the_reason(
        double weight,
        double minimum,
        double maximum,
        string reason)
    {
        var settings = new PrioritySettings(
            PriorityWeights.Default,
            [new("benefit", "Benefit", "", (decimal)weight, (decimal)minimum, (decimal)maximum)]);

        var refused = Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Contains("benefit", refused.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_criteria_with_one_id_are_refused_and_the_standard_ones_pass()
    {
        var duplicated = new PrioritySettings(
            PriorityWeights.Default,
            [new("benefit", "A", "", 1m, 0m, 10m), new("Benefit", "B", "", 1m, 0m, 10m)]);
        Assert.Throws<ArgumentException>(duplicated.Validate);

        new PrioritySettings(PriorityWeights.Default, PrioritySettings.StandardCriteria).Validate();
        PrioritySettings.SafeDefault.Validate();
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
            ["Task"],
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
    public void A_card_advances_one_stage_and_only_out_of_a_stage_that_ran()
    {
        var stages = new[]
        {
            Stage("backlog", 10),
            Stage("analysis", 20),
            Stage("implementation", 30),
            Stage("review", 40),
            Stage("done", 50)
        };

        // The backlog is where a card is created: leaving it is how work begins, and the start that follows
        // is what records the work.
        Assert.Null(CardProgress.RefuseForwardMove("TASK-1", "backlog", "analysis", stages, []));

        // One stage at a time: a card cannot be declared reviewed straight from the backlog. This is what an
        // agent did before the rule existed, and the card then sat in review with an empty runs tab.
        var skipped = CardProgress.RefuseForwardMove("TASK-1", "backlog", "review", stages, []);
        Assert.NotNull(skipped);
        Assert.Contains("one stage at a time", skipped, StringComparison.Ordinal);
        Assert.Contains("aiko_start_stage", skipped, StringComparison.Ordinal);

        // A stage nobody ran cannot be left: leaving it would make the card look worked when it is not.
        var unworked = CardProgress.RefuseForwardMove("TASK-1", "analysis", "implementation", stages, []);
        Assert.NotNull(unworked);
        Assert.Contains("no execution", unworked, StringComparison.Ordinal);

        // Neither can a stage that was started and abandoned: the owner's rule is "only a finished stage may be
        // left", and the refusal names the state the stage stopped in so continuing it is the visible way on.
        var abandoned = CardProgress.RefuseForwardMove(
            "TASK-1", "analysis", "implementation", stages,
            [new StageRun("analysis", StageExecutionState.Paused)]);
        Assert.NotNull(abandoned);
        Assert.Contains("not finished", abandoned, StringComparison.Ordinal);
        Assert.Contains("Paused", abandoned, StringComparison.Ordinal);
        Assert.NotNull(CardProgress.RefuseForwardMove(
            "TASK-1", "analysis", "implementation", stages,
            [new StageRun("analysis", StageExecutionState.Running)]));

        // The stage finished, so the card moves on.
        Assert.Null(CardProgress.RefuseForwardMove(
            "TASK-1", "analysis", "implementation", stages,
            [new StageRun("analysis", StageExecutionState.Completed)]));

        // Backwards, in place, or a stage this pipeline cannot place is not this rule's business.
        Assert.Null(CardProgress.RefuseForwardMove("TASK-1", "review", "implementation", stages, []));
        Assert.Null(CardProgress.RefuseForwardMove("TASK-1", "implementation", "implementation", stages, []));
        Assert.Null(CardProgress.RefuseForwardMove("TASK-1", "analysis", "unknown", stages, []));

        static StageDefinition Stage(string id, int order) => new(
            id,
            id,
            order,
            "Do it.",
            ["Task"],
            "claude",
            [],
            new Dictionary<string, ActionPolicy>(StringComparer.Ordinal),
            null);
    }

    /// <summary>
    /// A stage for the card-type tests, where only the id and the order matter.
    /// </summary>
    private static StageDefinition Stage(string id, int order) => new(
        id,
        id,
        order,
        "Do it.",
        ["Task"],
        "claude",
        [],
        new Dictionary<string, ActionPolicy>(StringComparer.Ordinal),
        null);

    [Fact]
    public void The_request_and_the_requirements_are_two_texts_read_from_the_metadata()
    {
        var card = TextCard("backlog") with
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Card.RequestMetadataKey] = "the dropdown is empty on Fridays",
                [Card.RequirementsMetadataKey] = "Make the dropdown list every value."
            }
        };

        Assert.Equal("the dropdown is empty on Fridays", card.Request);
        Assert.Equal("Make the dropdown list every value.", card.Requirements);

        // A card written before the request existed has none, and none is invented for it: "not recorded" and
        // "nothing was asked" are different facts.
        var old = TextCard("analysis");
        Assert.Null(old.Request);
        Assert.Null(old.Requirements);

        // Text is written trimmed, and a blank one removes the key rather than storing an empty string, so
        // "nothing is written" has exactly one representation.
        var written = Card.WithText(card.Metadata, Card.RequestMetadataKey, "  spaced out  ");
        Assert.Equal("spaced out", written[Card.RequestMetadataKey]);
        var cleared = Card.WithText(written, Card.RequestMetadataKey, "   ");
        Assert.False(cleared.ContainsKey(Card.RequestMetadataKey));
        Assert.True(cleared.ContainsKey(Card.RequirementsMetadataKey));
    }

    [Fact]
    public void The_original_request_is_fixed_once_the_card_leaves_the_backlog()
    {
        // A card nobody has taken into work can still be corrected: a typo, a clarification.
        Assert.Null(Card.RefuseRequestChange("TASK-1", "backlog"));

        // Once the card is in its pipeline the request records what was asked, and the refusal names the stage
        // and points at the text that may change instead.
        var refusal = Card.RefuseRequestChange("TASK-1", "analysis");
        Assert.NotNull(refusal);
        Assert.Contains("fixed", refusal, StringComparison.Ordinal);
        Assert.Contains("analysis", refusal, StringComparison.Ordinal);
        Assert.Contains("requirements", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_requirements_change_explains_itself_in_a_note()
    {
        // Nothing changed: no note, so the feed is not filled with non-events.
        Assert.Null(Card.RequirementsChangeNote("same text", "same text", "anything"));
        Assert.Null(Card.RequirementsChangeNote(null, null, "anything"));

        // A change carries the reason the caller gave, quoted.
        var explained = Card.RequirementsChangeNote("old", "new", "the list was unsorted");
        Assert.NotNull(explained);
        Assert.Contains("\"the list was unsorted\"", explained, StringComparison.Ordinal);

        // Clearing the text is a change too - the card no longer asks what it asked a moment ago - and without
        // a reason the note is still written, just without the quote.
        var cleared = Card.RequirementsChangeNote("old", null, null);
        Assert.NotNull(cleared);
        Assert.Contains("changed", cleared, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", cleared, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_waits_for_the_cards_that_block_it()
    {
        var workflows = new[]
        {
            new WorkflowDefinition(
                "task",
                "Tasks",
                [
                    Stage("backlog", 10),
                    Stage("analysis", 20),
                    Stage("done", 30)
                ],
                1)
        };

        static Card Card(string id, string stageId) => new(
            new CardReference("project", id),
            "Task",
            $"Title of {id}",
            "task",
            stageId,
            1,
            1m,
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));

        static CardRelation Edge(CardReference source, CardReference target, string type) =>
            new("relation-1", source, target, type, DateTimeOffset.UnixEpoch);

        var blocked = Card("TASK-2", "backlog");
        var blocker = Card("TASK-1", "backlog");
        var blocks = Edge(blocker.Reference, blocked.Reference, RelationTypes.Blocks);

        // The blocker is in its pipeline, so the card waits: the rule names the card, its title and where it is,
        // because that is what a person has to be told next.
        var unfinished = CardBlocking.Unfinished(blocked.Reference, [blocks], [blocked, blocker], workflows);
        var waiting = Assert.Single(unfinished);
        Assert.Equal("TASK-1", waiting.CardId);
        Assert.Equal("Title of TASK-1", waiting.Title);
        Assert.Equal("backlog", waiting.StageId);

        var refusal = CardBlocking.RefuseStart(blocked.Reference.CardId, unfinished);
        Assert.NotNull(refusal);
        Assert.Contains("'TASK-1'", refusal, StringComparison.Ordinal);
        Assert.Contains("Title of TASK-1", refusal, StringComparison.Ordinal);
        Assert.Contains("backlog", refusal, StringComparison.Ordinal);
        Assert.Contains("blocked", refusal, StringComparison.Ordinal);

        // The same blocker at the end of its own pipeline no longer holds anything back, and the message says
        // nothing: the last stage is read from the workflow, so a type whose end is not called 'done' works too.
        var finished = Card("TASK-1", "done");
        Assert.Empty(CardBlocking.Unfinished(blocked.Reference, [blocks], [blocked, finished], workflows));
        Assert.Null(CardBlocking.RefuseStart(blocked.Reference.CardId, []));

        // An edge of another type is not a block, and neither is an edge whose source card is not there: the
        // reindexer reports that defect, and a gate waiting for a card nobody can finish would stop the work.
        Assert.Empty(CardBlocking.Unfinished(
            blocked.Reference,
            [Edge(blocker.Reference, blocked.Reference, RelationTypes.RelatesTo)],
            [blocked, blocker],
            workflows));
        Assert.Empty(CardBlocking.Unfinished(blocked.Reference, [blocks], [blocked], workflows));

        // The direction matters: the card that blocks is the source, so the edge the other way round says
        // nothing about this card.
        Assert.Empty(CardBlocking.Unfinished(
            blocker.Reference,
            [blocks],
            [blocked, blocker],
            workflows));
    }

    private static Card TextCard(string stageId) => new(
        new CardReference("project", "TASK-1"),
        "Task",
        "Fix the dropdown",
        "task",
        stageId,
        1,
        1m,
        [],
        [],
        new Dictionary<string, string>(StringComparer.Ordinal));

    [Fact]
    public void Stage_executors_and_validation_commands_survive_the_workflow_json()
    {
        var stage = new StageDefinition(
            "implementation",
            "Implementation",
            30,
            "Implement the task.",
            ["Task"],
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
        var kindJson = JsonSerializer.Serialize("Task", JsonSerializerOptions.Web);
        Assert.Equal("\"Task\"", kindJson);
        Assert.Equal("Task", JsonSerializer.Deserialize<string>(kindJson, JsonSerializerOptions.Web));

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
        Assert.Equal("Story", CardKind.Canonical("story"));
        Assert.Equal("Task", CardKind.Canonical("TASK"));
        Assert.Equal("Epic", CardKind.Canonical("epic"));
    }

    [Fact]
    public void The_engine_names_no_card_types()
    {
        // A card type is the workflow a project declares, so the engine must not carry a single type name:
        // no constant string and no list of them. Any public static string member here would be exactly the
        // knowledge this rule removed.
        Assert.DoesNotContain(
            typeof(CardKind).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            field => field.FieldType == typeof(string));
        Assert.DoesNotContain(
            typeof(CardKind).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            property => property.PropertyType == typeof(string));
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

    [Fact]
    public void A_card_is_archived_by_the_mark_present_and_not_by_its_text()
    {
        var card = CardAt("done");
        Assert.False(card.IsArchived);
        Assert.Null(card.ArchivedAt);

        var at = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var archived = Archived(card, at);
        Assert.True(archived.IsArchived);
        Assert.Equal(at, archived.ArchivedAt);

        // A mark nobody can read still means the card was put away: reading it as "on the board" would hand
        // the card back to the queue on the strength of a typo.
        var unreadable = card with { Metadata = Mark("yesterday") };
        Assert.True(unreadable.IsArchived);
        Assert.Null(unreadable.ArchivedAt);

        // Clearing the mark is the only way back, and it leaves no empty key behind.
        var restored = card with { Metadata = Card.WithArchivedAt(archived.Metadata, null) };
        Assert.False(restored.IsArchived);
        Assert.DoesNotContain(Card.ArchivedAtMetadataKey, restored.Metadata.Keys);
    }

    [Fact]
    public void Finishing_is_the_end_of_a_cards_own_pipeline_and_not_the_name_done()
    {
        var pipeline = Pipeline("shipped");

        Assert.True(CardCompletion.IsFinished(pipeline, "shipped", StageExecutionState.Completed));

        // The last stage without a finished run is work left to do, whoever is standing in it.
        Assert.False(CardCompletion.IsFinished(pipeline, "shipped", StageExecutionState.Running));
        Assert.False(CardCompletion.IsFinished(pipeline, "shipped", null));

        // A stage called 'done' that this pipeline does not end with is the end of nothing.
        Assert.False(CardCompletion.IsFinished(pipeline, "done", StageExecutionState.Completed));

        // A middle stage with a finished run is still work: the next stage is what it waits for.
        Assert.False(CardCompletion.IsFinished(pipeline, "backlog", StageExecutionState.Completed));

        // A card whose type has no pipeline has no end to have reached.
        Assert.False(CardCompletion.IsFinished(null, "shipped", StageExecutionState.Completed));
    }

    [Fact]
    public void The_archive_gate_answers_a_repeated_action_first()
    {
        // The card is finished, so every other check would let it through; the mark is what makes the answer
        // "already there" rather than "you may".
        var refusal = CardArchiving.Refuse(
            Archived(CardAt("done"), DateTimeOffset.UnixEpoch),
            Pipeline("done"),
            [new StageRun("done", StageExecutionState.Completed)]);

        Assert.NotNull(refusal);
        Assert.Contains("already in the archive", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_archive_gate_refuses_a_card_whose_stage_is_still_open()
    {
        var pipeline = Pipeline("done");

        // The gate reads the latest run of a stage, and NeedsAttention is a stage left hanging: putting the
        // card away would hide the work someone has to come back to.
        var refusal = CardArchiving.Refuse(
            CardAt("done"),
            pipeline,
            [new StageRun("done", StageExecutionState.NeedsAttention)]);

        Assert.NotNull(refusal);
        Assert.Contains("open run", refusal, StringComparison.Ordinal);
        Assert.Contains("NeedsAttention", refusal, StringComparison.Ordinal);

        // A run that closed the stage is not open, so the same card is accepted.
        Assert.Null(CardArchiving.Refuse(
            CardAt("done"),
            pipeline,
            [new StageRun("done", StageExecutionState.Completed)]));
    }

    [Fact]
    public void The_archive_gate_refuses_a_card_that_has_not_reached_the_end()
    {
        var pipeline = Pipeline("done");

        // Mid-pipeline: the refusal names the stage the card sits in and the last stage it should reach.
        var midway = CardArchiving.Refuse(
            CardAt("backlog"),
            pipeline,
            [new StageRun("backlog", StageExecutionState.Completed)]);

        Assert.NotNull(midway);
        Assert.Contains("not finished", midway, StringComparison.Ordinal);
        Assert.Contains("'done'", midway, StringComparison.Ordinal);

        // A card nobody started says so, instead of naming a run state that does not exist.
        var untouched = CardArchiving.Refuse(CardAt("done"), pipeline, []);
        Assert.NotNull(untouched);
        Assert.Contains("not started", untouched, StringComparison.Ordinal);

        // No pipeline at all: there is no end for the card to have reached.
        Assert.NotNull(CardArchiving.Refuse(CardAt("done"), null, []));
    }

    [Fact]
    public void An_archived_card_is_not_taken_into_work()
    {
        Assert.Null(CardArchiving.RefuseWork(CardAt("done")));

        var refusal = CardArchiving.RefuseWork(Archived(CardAt("done"), DateTimeOffset.UnixEpoch));
        Assert.NotNull(refusal);
        Assert.Contains("aiko_restore_card", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_board_leaves_archived_cards_out()
    {
        var onBoard = CardAt("done", "TASK-1");
        var putAway = Archived(CardAt("backlog", "TASK-2"), DateTimeOffset.UnixEpoch);

        var shown = CardArchiving.OnBoard([onBoard, putAway]);

        var only = Assert.Single(shown);
        Assert.Equal(onBoard.Reference.CardId, only.Reference.CardId);
    }

    [Fact]
    public void A_card_at_the_end_of_its_pipeline_does_not_block_another_one()
    {
        // The two rules hold together, and this is where they are pinned: CardBlocking counts a blocker that
        // reached the end of its pipeline as finished, and the archive accepts nothing but such a card - so
        // putting a card away cannot hide a blockage, because an archivable card blocked nobody.
        var pipeline = Pipeline("done");
        var blocker = CardAt("done", "TASK-1");
        var waiting = CardAt("backlog", "TASK-2");
        var relation = new CardRelation(
            "relation-1",
            blocker.Reference,
            waiting.Reference,
            RelationTypes.Blocks,
            DateTimeOffset.UnixEpoch);

        Assert.Empty(CardBlocking.Unfinished(waiting.Reference, [relation], [blocker, waiting], [pipeline]));
        Assert.Null(CardArchiving.Refuse(
            blocker,
            pipeline,
            [new StageRun("done", StageExecutionState.Completed)]));
    }

    /// <summary>A card in the given stage, with an id of its own so a pair of them stay apart.</summary>
    private static Card CardAt(string stageId, string cardId = "TASK-1") =>
        new(
            new CardReference("project-1", cardId),
            "Task",
            "A card",
            "task",
            stageId,
            1,
            0m,
            [],
            [],
            Mark(null));

    /// <summary>Metadata carrying the archive mark with the given text, or none when it is null.</summary>
    private static IReadOnlyDictionary<string, string> Mark(string? at) =>
        at is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { [Card.ArchivedAtMetadataKey] = at };

    /// <summary>The card with the archive mark set, as the archive action would save it.</summary>
    private static Card Archived(Card card, DateTimeOffset at) =>
        card with { Metadata = Card.WithArchivedAt(card.Metadata, at) };

    /// <summary>A two-stage pipeline whose last stage is named as asked, so 'done' is never assumed.</summary>
    private static WorkflowDefinition Pipeline(string lastStageId) =>
        new(
            "task",
            "Tasks",
            [
                new StageDefinition(
                    "backlog", "Backlog", 10, "Start", ["Task"], null, [], new Dictionary<string, ActionPolicy>()),
                new StageDefinition(
                    lastStageId, lastStageId, 20, "Finish", ["Task"], null, [], new Dictionary<string, ActionPolicy>())
            ],
            1);
}
