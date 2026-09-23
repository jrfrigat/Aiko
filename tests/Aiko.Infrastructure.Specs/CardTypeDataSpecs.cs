using System.Text.Json;
using Aiko.Application.Prioritization;
using Aiko.Domain.Cards;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the rule the project owner set: a card type is project data, so the engine's own behaviour never
/// keys off a type's name.
/// </summary>
/// <remarks>
/// These specs are deliberately written with names the engine never shipped (<c>widget</c>, <c>gadget</c>): a
/// name the template happens to use would prove nothing about the engine not knowing it.
/// </remarks>
public sealed class CardTypeDataSpecs
{
    [Theory]
    [InlineData("widget", "Widgets")]
    [InlineData("gadget", "Gadget cards")]
    [InlineData("category", "Categories")]
    public void A_type_is_filed_under_the_name_of_its_workflow(string workflowId, string title)
    {
        using var project = new TempProject();
        WriteWorkflow(project.Root, workflowId, title);

        Assert.Equal(title, FileCardStore.CollectionFor(project.Root, CardKind.FromWorkflowId(workflowId)));
    }

    [Theory]
    [InlineData("story", "stories")]
    [InlineData("category", "categories")]
    [InlineData("box", "boxes")]
    [InlineData("branch", "branches")]
    [InlineData("day", "days")]
    public void The_older_name_still_follows_the_plural_rule(string id, string expected) =>
        Assert.Equal(expected, FileCardStore.Pluralize(id));

    [Fact]
    public void A_type_without_a_workflow_keeps_the_name_it_used_to_have()
    {
        using var project = new TempProject();

        // A project whose definition was removed still holds the cards that were filed under it, so the older
        // name is the one to look under rather than no name at all.
        Assert.Equal("categories", FileCardStore.CollectionFor(project.Root, "Category"));
    }

    [Fact]
    public void A_title_that_cannot_be_a_directory_name_is_made_fit_for_one()
    {
        // A title is free text from the settings; the name stays the project's own and is only made fit to be
        // a folder.
        Assert.Equal("Bugs - issues", FileCardStore.SanitizeCollectionName("  Bugs / issues  "));
        Assert.Equal("Notes", FileCardStore.SanitizeCollectionName("Notes."));
        Assert.Equal(string.Empty, FileCardStore.SanitizeCollectionName("   "));
        Assert.Equal(string.Empty, FileCardStore.SanitizeCollectionName(null));
    }

    [Fact]
    public void A_card_filed_under_the_older_name_is_found_where_it_lies()
    {
        using var project = new TempProject();
        WriteWorkflow(project.Root, "task", "Tasks");
        var older = Path.Combine(AikoProjectPaths.CardCollectionsRoot(project.Root), "tasks", "TASK-1");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, "card.json"), "{}");

        // The workflow asks for `Tasks`, and the card is where the older rule filed it: reading is not the
        // moment to move a project around, so the card is found where it lies.
        Assert.Equal("Tasks", FileCardStore.CollectionFor(project.Root, "Task"));
        Assert.Equal(older, FileCardStore.FindCardDirectory(project.Root, "TASK-1"));
        Assert.Contains(
            "tasks",
            Directory.EnumerateDirectories(AikoProjectPaths.CardCollectionsRoot(project.Root))
                .Select(Path.GetFileName)
                .ToArray());
    }

    [Fact]
    public void Writing_a_card_brings_its_collection_to_the_name_the_workflow_has()
    {
        using var project = new TempProject();
        WriteWorkflow(project.Root, "task", "Tasks");
        Directory.CreateDirectory(Path.Combine(AikoProjectPaths.CardCollectionsRoot(project.Root), "tasks"));

        var directory = FileCardStore.GetCardDirectory(project.Root, "TASK-1", "Task");

        Assert.Equal("Tasks", Path.GetFileName(Path.GetDirectoryName(directory)));
        // The stored spelling, not the requested one: on a filesystem that ignores case the folder has to be
        // renamed to actually read as the workflow's name.
        Assert.Contains(
            "Tasks",
            Directory.EnumerateDirectories(AikoProjectPaths.CardCollectionsRoot(project.Root))
                .Select(Path.GetFileName)
                .ToArray());
    }

    [Fact]
    public void A_card_blends_with_its_parent_only_when_its_type_says_so()
    {
        const string projectId = "project";
        var parent = MakeCard(projectId, "PARENT", "Widget", "widget", 10m);
        var blendingChild = MakeCard(projectId, "CHILD-A", "Widget", "widget", 4m);
        var keepingChild = MakeCard(projectId, "CHILD-B", "Gadget", "gadget", 4m);
        var relations = new CardRelation[]
        {
            new("r1", parent.Reference, blendingChild.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow),
            new("r2", parent.Reference, keepingChild.Reference, RelationTypes.ParentChild, DateTimeOffset.UtcNow)
        };

        var priorities = CardPriorityProjector.Project(
            [parent, blendingChild, keepingChild],
            relations,
            PrioritySettings.SafeDefault,
            [
                new WorkflowDefinition("widget", "Widgets", [], 1, BlendsWithParent: true),
                new WorkflowDefinition("gadget", "Gadgets", [], 1)
            ]);

        // The type whose workflow declares the flag rolls its score up into the parent...
        var blended = priorities.Single(priority => priority.CardId == "CHILD-A").Snapshot;
        Assert.Equal(0.1m, blended.MaximumParentPriority);

        // ...and the type that does not keeps its own score, whatever it is called.
        var kept = priorities.Single(priority => priority.CardId == "CHILD-B").Snapshot;
        Assert.Null(kept.MaximumParentPriority);
        Assert.Equal(0.04m, kept.EffectivePriority);
    }

    private static Card MakeCard(
        string projectId,
        string cardId,
        string kind,
        string workflowId,
        decimal ownPriority) =>
        new(
            new CardReference(projectId, cardId),
            kind,
            cardId,
            workflowId,
            WorkflowDefinition.BacklogStageId,
            1,
            ownPriority,
            [],
            [],
            new Dictionary<string, string>());

    /// <summary>Writes one workflow document, the way the project's own store does.</summary>
    private static void WriteWorkflow(string projectRoot, string workflowId, string title)
    {
        var directory = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "workflows");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{workflowId}.json"),
            JsonSerializer.Serialize(
                new WorkflowDefinition(workflowId, title, [], 1),
                ProjectJsonContext.Default.WorkflowDefinition));
    }

    /// <summary>A throwaway project root, so a spec needs no project of its own.</summary>
    private sealed class TempProject : IDisposable
    {
        public TempProject()
        {
            Root = Path.Combine(Path.GetTempPath(), "aiko-card-type-specs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp directory that would not go away is not a failure of the rule under test.
            }
        }
    }
}
