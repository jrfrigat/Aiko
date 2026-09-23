using System.Text.Json.Nodes;
using Aiko.Domain.Cards;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Discussion;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// A card keeps one directory when its type is renamed. The collection a card is filed under follows the
/// workflow's title, so renaming the title moves where new writes want to go - and everything that files
/// something beside a card (its notes, its artifacts, its handoffs) has to find the card where it actually is
/// rather than create the place it will be. Otherwise the card ends up in two directories and an old revision
/// is read back as the current one.
/// </summary>
public sealed class CardTypeRenameSpecs
{
    [Fact]
    public async Task Renaming_the_type_twice_with_a_note_between_saves_keeps_one_card_and_its_latest_revision()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var discussion = new FileCardDiscussionStore(context.Catalog, context.Cards);
            var card = InfrastructureSpecs.CreateCard(context.Project.Id, "TASK-1", 1);
            await context.Cards.SaveAsync(card, 0, CancellationToken.None);

            RenameWorkflow(context.ProjectRoot, "task", "Chores");
            await discussion.AppendAsync(card.Reference, "spec", "first note", CancellationToken.None);
            card = card with { Revision = 2, Title = "second" };
            await context.Cards.SaveAsync(card, 1, CancellationToken.None);

            RenameWorkflow(context.ProjectRoot, "task", "Errands");
            await discussion.AppendAsync(card.Reference, "spec", "second note", CancellationToken.None);
            card = card with { Revision = 3, Title = "third" };
            await context.Cards.SaveAsync(card, 2, CancellationToken.None);

            // One card document, wherever the renames left it...
            var documents = Directory
                .EnumerateFiles(AikoProjectPaths.CardCollectionsRoot(context.ProjectRoot), "card.json", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "TASK-1")
                .ToArray();
            Assert.Single(documents);

            // ...holding the latest revision, with both notes beside it.
            var stored = await context.Cards.FindAsync(card.Reference, CancellationToken.None);
            Assert.Equal(3, stored!.Revision);
            Assert.Equal("third", stored.Title);
            var notes = await discussion.ListAsync(card.Reference, CancellationToken.None);
            Assert.Equal(["first note", "second note"], notes.Select(note => note.Body).ToArray());
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(documents[0])!, "discussion.json")));
        });
    }

    [Fact]
    public async Task A_note_about_a_card_that_does_not_exist_is_refused_and_leaves_no_directory()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var discussion = new FileCardDiscussionStore(context.Catalog, context.Cards);
            var missing = new CardReference(context.Project.Id, "TASK-404");

            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await discussion.AppendAsync(missing, "spec", "hello", CancellationToken.None));
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await discussion.ListAsync(missing, CancellationToken.None));

            // A later card with the same id must not inherit a directory - or notes - nobody created it for.
            var root = AikoProjectPaths.CardCollectionsRoot(context.ProjectRoot);
            Assert.Empty(Directory.Exists(root)
                ? Directory.EnumerateDirectories(root, "TASK-404", SearchOption.AllDirectories)
                : []);
        });
    }

    [Fact]
    public void A_card_found_in_two_collections_is_read_from_the_copy_with_the_higher_revision()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs.Duplicates", Guid.NewGuid().ToString("N"));
        try
        {
            var collections = AikoProjectPaths.CardCollectionsRoot(root);
            WriteRevision(Path.Combine(collections, "Alpha", "TASK-7"), 2);
            WriteRevision(Path.Combine(collections, "Beta", "TASK-7"), 5);

            // Whatever order the directories are listed in, the newer copy is the card.
            Assert.Equal(
                Path.Combine(collections, "Beta", "TASK-7"),
                FileCardStore.FindCardDirectory(root, "TASK-7"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteRevision(string directory, long revision)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "card.json"), $$"""{ "revision": {{revision}} }""");
    }

    private static void RenameWorkflow(string projectRoot, string workflowId, string title)
    {
        var path = Path.Combine(AikoProjectPaths.DataRoot(projectRoot), "workflows", $"{workflowId}.json");
        var workflow = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        workflow["title"] = title;
        File.WriteAllText(path, workflow.ToJsonString());
    }
}
