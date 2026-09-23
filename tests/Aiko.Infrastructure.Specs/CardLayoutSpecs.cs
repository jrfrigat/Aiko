using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards where a card is filed. Cards live under <c>.aiko/workflows</c>, beside the workflow definitions
/// that describe their pipeline - and a directory there is a collection of cards while a <c>.json</c> file is
/// a definition. That rule is what lets a project name a type anything at all: a type named <c>Workflow</c>
/// files its cards in <c>workflows/workflows/</c> without touching <c>workflows/workflow.json</c>, where a
/// type named after a service directory used to land on top of the definitions and disappear from the board.
/// </summary>
/// <remarks>
/// The project in these specs is the one the infrastructure specs build: the fixture is shared rather than
/// copied, so both files always test against the same project shape.
/// </remarks>
public sealed class CardLayoutSpecs
{
    private static readonly JsonSerializerOptions CardJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_card_is_filed_under_the_workflows_directory_and_its_definition_stays_beside_it()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var card = InfrastructureSpecs.CreateCard(context.Project.Id, "TASK-1", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var cardPath = Path.Combine(
                context.StitchRoot,
                AikoProjectPaths.WorkflowsDirectoryName,
                "tasks",
                "TASK-1",
                "card.json");
            Assert.True(File.Exists(cardPath), "the card is not filed under .aiko/workflows");

            // The definition of the same type sits in the same directory as a file, and the collection as a
            // directory: the two are told apart by shape, which is why neither can shadow the other.
            Assert.True(File.Exists(Path.Combine(context.StitchRoot, "workflows", "task.json")));
            Assert.False(Directory.Exists(Path.Combine(context.StitchRoot, "tasks")));

            var reindexed = await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(1, reindexed.Cards);
            Assert.Single(await context.Cards.ListAsync(context.Project.Id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_type_whose_collection_is_named_after_a_service_directory_works()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            // "Workflow" is the sharp case: its collection is "workflows", the very directory the definitions
            // live in. Before cards moved, such a card was written among the definitions and the collection
            // scan skipped it - the type was unusable and nothing said so.
            var card = InfrastructureSpecs.CreateCard(context.Project.Id, "FLOW-1", 1) with
            {
                Kind = "Workflow",
                WorkflowId = "workflow"
            };
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            var cardPath = Path.Combine(
                context.StitchRoot,
                "workflows",
                "workflows",
                "FLOW-1",
                "card.json");
            Assert.True(File.Exists(cardPath), "a type named like a service directory is not filed");

            var found = await context.Cards.FindAsync(card.Reference, CancellationToken.None);
            Assert.Equal("FLOW-1", found?.Reference.CardId);

            var reindexed = await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(1, reindexed.Cards);
        });
    }

    [Fact]
    public async Task A_card_filed_the_old_way_is_still_read()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var reference = new CardReference(context.Project.Id, "OLD-1");
            WriteCardInTheOldPlace(context, reference.CardId, kind: "Task", title: "Filed the old way");

            // Reading a project that has not been migrated yet is the whole point of the fallback: the file is
            // where it always was, so that is where the card is found.
            var found = await context.Cards.FindAsync(reference, CancellationToken.None);
            Assert.Equal("Filed the old way", found?.Title);

            // Reindexing must not lose it either - a rebuild is what a project does after an update.
            var reindexed = await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(1, reindexed.Cards);
            Assert.Single(await context.Cards.ListAsync(context.Project.Id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Saving_a_card_filed_the_old_way_moves_the_whole_directory()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var reference = new CardReference(context.Project.Id, "OLD-2");
            var oldDirectory = WriteCardInTheOldPlace(context, reference.CardId, kind: "Task", title: "Before");
            // Something filed beside the card, of the kind a card directory holds: an artifact, a discussion,
            // a handoff. It travels with the card, because the directory is what moves.
            await File.WriteAllTextAsync(Path.Combine(oldDirectory, "analysis.md"), "# Analysis\n");

            var current = await context.Cards.FindAsync(reference, CancellationToken.None);
            Assert.NotNull(current);
            await context.Cards.SaveAsync(
                current! with { Title = "After", Revision = current.Revision + 1 },
                current.Revision,
                CancellationToken.None);

            var newDirectory = Path.Combine(
                context.StitchRoot,
                "workflows",
                "tasks",
                reference.CardId);
            Assert.False(Directory.Exists(oldDirectory), "the card was left behind in the old place");
            Assert.True(File.Exists(Path.Combine(newDirectory, "card.json")));
            Assert.True(
                File.Exists(Path.Combine(newDirectory, "analysis.md")),
                "the artifact did not travel with the card");

            var saved = await context.Cards.FindAsync(reference, CancellationToken.None);
            Assert.Equal("After", saved?.Title);
        });
    }

    [Fact]
    public async Task A_card_filed_the_old_way_keeps_its_notes_beside_it()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(context =>
        {
            var cardId = "OLD-3";
            var oldDirectory = WriteCardInTheOldPlace(context, cardId, kind: "Task", title: "Old");

            // Whoever writes beside a card hands over the card's type - so the answer has to be the directory
            // the card is in, not the one it would be in had it been migrated. Looking in the old root only
            // for the collections the new root lacks would make this answer wrong, and the notes would land
            // in a directory the card is not in.
            Assert.Equal(
                oldDirectory,
                FileCardStore.FindCardDirectory(context.ProjectRoot, cardId));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_migration_files_a_whole_collection_under_workflows()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var reference = new CardReference(context.Project.Id, "MIG-1");
            var oldDirectory = WriteCardInTheOldPlace(context, reference.CardId, kind: "Task", title: "Old");
            await File.WriteAllTextAsync(Path.Combine(oldDirectory, "analysis.md"), "# Analysis\n");
            Directory.CreateDirectory(Path.Combine(oldDirectory, "handoffs"));

            var migration = CardLayoutMigrator.Migrate(context.ProjectRoot);

            Assert.Equal(["tasks"], migration.Moved);
            Assert.Empty(migration.Failed);
            Assert.False(Directory.Exists(oldDirectory), "the collection was left behind");
            var newDirectory = Path.Combine(context.StitchRoot, "workflows", "tasks", reference.CardId);
            Assert.True(File.Exists(Path.Combine(newDirectory, "card.json")));
            Assert.True(File.Exists(Path.Combine(newDirectory, "analysis.md")));
            Assert.True(Directory.Exists(Path.Combine(newDirectory, "handoffs")));

            // Idempotent: a second pass has nothing to move, and the project reads exactly as before.
            var again = CardLayoutMigrator.Migrate(context.ProjectRoot);
            Assert.Empty(again.Moved);
            Assert.Equal("Old", (await context.Cards.FindAsync(reference, CancellationToken.None))?.Title);
            var reindexed = await context.Reindexer.ReindexAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal(1, reindexed.Cards);
        });
    }

    [Fact]
    public async Task The_migration_leaves_everything_that_is_not_a_card_collection_alone()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(context =>
        {
            // A directory somebody made by hand, and the service directories init creates: none of them holds
            // a card, so none of them is a collection and none of them is touched.
            Directory.CreateDirectory(Path.Combine(context.StitchRoot, "notes"));
            Assert.Empty(CardLayoutMigrator.PlannedMoves(context.ProjectRoot));

            var migration = CardLayoutMigrator.Migrate(context.ProjectRoot);

            Assert.Empty(migration.Moved);
            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "notes")));
            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "runtime")));
            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "memory")));
            Assert.True(Directory.Exists(Path.Combine(context.StitchRoot, "projections")));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_doctor_names_the_cards_filed_the_old_way_and_stops_after_the_move()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            WriteCardInTheOldPlace(context, "MIG-2", kind: "Task", title: "Old");
            var doctor = BuildDoctor(context);

            var before = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);
            var finding = Assert.Single(before.Findings, item => item.Area == "card-layout");
            Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
            Assert.Contains("repair --fix", finding.Summary, StringComparison.Ordinal);

            CardLayoutMigrator.Migrate(context.ProjectRoot);

            var after = await doctor.InspectAsync(context.Project.Id, CancellationToken.None);
            Assert.DoesNotContain(after.Findings, item => item.Area == "card-layout");
        });
    }

    /// <summary>
    /// The doctor with the least it needs - no agent adapters and no saved port - so its project checks are
    /// what answers. The construction follows the doctor specs beside the infrastructure ones.
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

    /// <summary>
    /// Files a card the way every project did before cards moved: in the collection directly below
    /// <c>.aiko</c>. Returns that directory.
    /// </summary>
    private static string WriteCardInTheOldPlace(
        TestContext context,
        string cardId,
        string kind,
        string title)
    {
        var directory = Path.Combine(context.StitchRoot, "tasks", cardId);
        Directory.CreateDirectory(directory);
        var card = InfrastructureSpecs.CreateCard(context.Project.Id, cardId, 1) with
        {
            Kind = kind,
            Title = title
        };
        File.WriteAllText(
            Path.Combine(directory, "card.json"),
            JsonSerializer.Serialize(card, CardJson));
        return directory;
    }
}
