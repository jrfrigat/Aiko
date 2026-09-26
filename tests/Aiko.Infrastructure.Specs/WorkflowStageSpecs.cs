using System.Text.Json.Nodes;
using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the reserved-stage rule: every pipeline begins with <c>backlog</c> and ends with <c>done</c>, and the
/// engine enforces it rather than trusting whoever wrote the document.
/// </summary>
/// <remarks>
/// The end of a pipeline used to be whatever stage carried the greatest order, which made "finished",
/// "archivable" and "no longer blocking" depend on what an author typed. The rule now lives in
/// <see cref="WorkflowDefinition"/>, is asked when a workflow is written, is asked again when one is read, and
/// is applied to projects that predate it by <see cref="WorkflowStageMigrator"/>. Each of those is pinned here,
/// because each of them is the only thing standing between a broken file and a board that quietly has no end.
/// </remarks>
public sealed class WorkflowStageSpecs
{
    [Fact]
    public async Task A_workflow_that_does_not_end_with_done_is_refused_when_it_is_written()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = new FileProjectDefinitionStore(context.Catalog);
            var workflow = (await store.ReadAsync(context.Project.Id, CancellationToken.None))
                .Workflows.Single(item => item.Id == "task");

            // The pipeline ends with a stage that is not the reserved one: the engine reads no end there.
            var renamed = workflow with
            {
                Stages = workflow.Stages
                    .Select(stage => WorkflowDefinition.IsDone(stage) ? stage with { Id = "shipped" } : stage)
                    .ToArray(),
                Revision = workflow.Revision + 1
            };

            var refusal = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SaveWorkflowAsync(
                    context.Project.Id,
                    renamed,
                    workflow.Revision,
                    CancellationToken.None));
            Assert.Contains(WorkflowDefinition.DoneStageId, refusal.Message, StringComparison.Ordinal);

            // The done stage is there but no longer last.
            var last = workflow.Stages.Max(stage => stage.Order);
            var middle = workflow.Stages
                .Where(stage => !WorkflowDefinition.IsBacklog(stage) && !WorkflowDefinition.IsDone(stage))
                .MinBy(stage => stage.Order)!;
            var notLast = workflow with
            {
                Stages = workflow.Stages
                    .Select(stage => StringComparer.Ordinal.Equals(stage.Id, middle.Id)
                        ? stage with { Order = last + 10 }
                        : stage)
                    .ToArray(),
                Revision = workflow.Revision + 1
            };

            var lastRefusal = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SaveWorkflowAsync(
                    context.Project.Id,
                    notLast,
                    workflow.Revision,
                    CancellationToken.None));
            Assert.Contains("last stage", lastRefusal.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_document_that_breaks_the_rule_is_refused_when_it_is_read()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await RemoveStageAsync(context, "task", WorkflowDefinition.DoneStageId);

            var store = new FileProjectDefinitionStore(context.Catalog);
            var refusal = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ReadAsync(context.Project.Id, CancellationToken.None));

            // The person is told which file, why, and what converges the project.
            Assert.Contains("task.json", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(WorkflowDefinition.DoneStageId, refusal.Message, StringComparison.Ordinal);
            Assert.Contains("repair --fix", refusal.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_project_that_predates_the_rule_is_brought_to_it()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            // The task pipeline as a project authored before the rule would have it: no done stage at all, and
            // the backlog is not the step a card enters at.
            await EditStagesAsync(context, "task", stages =>
            {
                Remove(stages, WorkflowDefinition.DoneStageId);
                var backlog = stages.First(stage =>
                    StringComparer.Ordinal.Equals((string?)stage!["id"], WorkflowDefinition.BacklogStageId))!;
                backlog["order"] = stages
                    .Select(stage => (int?)stage!["order"] ?? 0)
                    .DefaultIfEmpty(0)
                    .Max() + 10;
            });

            var plan = WorkflowStageMigrator.Plan(context.ProjectRoot);
            Assert.Equal(["task"], plan.Repairable);
            Assert.Empty(plan.Unrepairable);

            var migration = WorkflowStageMigrator.Migrate(context.ProjectRoot);
            Assert.Equal(["task"], migration.Repaired);
            Assert.Empty(migration.Failed);

            // The document reads again, and the pipeline is in the reserved shape: the backlog enters it and
            // the done stage ends it, with a stage a card of that type can actually be finished in.
            var store = new FileProjectDefinitionStore(context.Catalog);
            var definition = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var workflow = definition.Workflows.Single(item => item.Id == "task");
            var ordered = workflow.Stages.OrderBy(stage => stage.Order).ToArray();
            Assert.True(WorkflowDefinition.IsBacklog(ordered[0]));
            Assert.True(WorkflowDefinition.IsDone(ordered[^1]));
            Assert.Contains("Task", ordered[^1].AllowedCardKinds);
            Assert.Equal("done-all", ordered[^1].Icon);
            Assert.Equal("success", ordered[^1].Color);

            // The other pipelines were already in the shape and were left alone.
            Assert.Equal(3, definition.Workflows.Count);

            // Idempotent, which is what lets it run on every start and every repair.
            Assert.Empty(WorkflowStageMigrator.Plan(context.ProjectRoot).Repairable);
            Assert.Empty(WorkflowStageMigrator.Migrate(context.ProjectRoot).Repaired);
        });
    }

    [Fact]
    public async Task A_workflow_without_a_backlog_is_not_invented_for()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await RemoveStageAsync(context, "story", WorkflowDefinition.BacklogStageId);

            // The backlog is where a card enters its workflow and what its first stage says belongs to the
            // author, so a repair does not make one up: it names the document instead.
            var plan = WorkflowStageMigrator.Plan(context.ProjectRoot);
            Assert.Empty(plan.Repairable);
            Assert.Equal(["story"], plan.Unrepairable);

            var migration = WorkflowStageMigrator.Migrate(context.ProjectRoot);
            Assert.Empty(migration.Repaired);
            Assert.Equal(["story"], migration.Failed);

            // Nothing was written, so the document that was unreadable stays exactly as the person left it.
            var store = new FileProjectDefinitionStore(context.Catalog);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ReadAsync(context.Project.Id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task The_doctor_names_a_broken_pipeline_and_stops_after_the_repair()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await RemoveStageAsync(context, "task", WorkflowDefinition.DoneStageId);
            var doctor = BuildDoctor(context);

            // The diagnosis survives a definition the engine refuses to read - the very state it has to
            // describe - and the card-progress checks step aside rather than killing the report.
            var before = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);
            var finding = Assert.Single(before.Findings, item => item.Area == "workflow-stages");
            Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
            Assert.Contains("repair --fix", finding.Summary, StringComparison.Ordinal);

            WorkflowStageMigrator.Migrate(context.ProjectRoot);

            var after = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);
            Assert.DoesNotContain(after.Findings, item => item.Area == "workflow-stages");
        });
    }

    /// <summary>Drops one stage from a workflow document, as a person editing the file would.</summary>
    private static Task RemoveStageAsync(TestContext context, string workflowId, string stageId)
    {
        return EditStagesAsync(context, workflowId, stages => Remove(stages, stageId));
    }

    private static void Remove(JsonArray stages, string stageId)
    {
        for (var index = stages.Count - 1; index >= 0; index--)
        {
            if (StringComparer.Ordinal.Equals((string?)stages[index]!["id"], stageId))
            {
                stages.RemoveAt(index);
            }
        }
    }

    /// <summary>Rewrites one workflow's stages on disk, the way a person editing the file would.</summary>
    private static async Task EditStagesAsync(TestContext context, string workflowId, Action<JsonArray> edit)
    {
        var path = Path.Combine(context.StitchRoot, "workflows", $"{workflowId}.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        edit(document["stages"]!.AsArray());
        await File.WriteAllTextAsync(path, document.ToJsonString());
    }

    /// <summary>
    /// The doctor with the least it needs - no agent adapters and no saved port - so its project checks are what
    /// answers. The construction follows the layout specs beside this file.
    /// </summary>
    private static WorkshopDoctor BuildDoctor(TestContext context)
    {
        var dataPaths = new AikoDataPaths(context.Database.DatabasePath);
        return new WorkshopDoctor(
            dataPaths,
            context.Catalog,
            new UnifiedAgentInstaller([], context.Catalog, new FileProjectDefinitionStore(context.Catalog)),
            [],
            new DaemonEndpointConfiguration(dataPaths),
            new AccessTokenStore(dataPaths),
            context.Cards,
            new FileProjectDefinitionStore(context.Catalog),
            context.Executions);
    }
}
