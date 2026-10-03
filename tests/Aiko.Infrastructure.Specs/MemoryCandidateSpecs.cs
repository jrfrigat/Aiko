using Aiko.Application.Contracts;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Memory;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Memory candidates are a queue of decisions, not memory: a proposal waits beside its card until a person
/// approves it, and only the approval writes the durable memory.
/// </summary>
public sealed class MemoryCandidateSpecs
{
    [Fact]
    public async Task A_proposal_waits_beside_the_card_and_writes_no_memory()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await SaveCardAsync(context, "TASK-1");
            var candidates = Store(context);

            var proposed = await candidates.ProposeAsync(
                context.Project.Id,
                "TASK-1",
                "decisions/auth.md",
                "# Auth\n\nUse the token.",
                "which auth approach we settled on",
                "cline",
                CancellationToken.None);

            // Proposed, not stored: the status is Pending and the proposed document is not in the memory.
            Assert.Equal(MemoryCandidateStatus.Pending, proposed.Status);
            Assert.StartsWith("mc_", proposed.Id, StringComparison.Ordinal);
            Assert.Null(await context.Memory.ReadAsync(
                context.Project.Id, "decisions/auth.md", CancellationToken.None));

            // The document waits beside the card, where the work that proposed it is read.
            var path = Path.Combine(
                CardDirectory(context, "TASK-1"), "memory-candidates", proposed.Id + ".json");
            Assert.True(File.Exists(path), "The candidate should be a JSON document beside the card.");

            var listed = await candidates.ListAsync(context.Project.Id, "TASK-1", CancellationToken.None);
            Assert.Single(listed);
            Assert.Equal(proposed.Id, listed[0].Id);
        });
    }

    [Fact]
    public async Task Approving_writes_the_document_into_the_memory_and_rejecting_leaves_it_alone()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await SaveCardAsync(context, "TASK-1");
            var candidates = Store(context);

            var approved = await candidates.ProposeAsync(
                context.Project.Id,
                "TASK-1",
                "conventions/format.md",
                "# Format",
                "why",
                "cline",
                CancellationToken.None);
            var decided = await candidates.ApproveAsync(
                context.Project.Id, "TASK-1", approved.Id, "person", "looks right", CancellationToken.None);

            Assert.Equal(MemoryCandidateStatus.Approved, decided.Status);
            Assert.NotNull(decided.ResolvedAt);
            Assert.Equal("person", decided.ResolvedBy);

            // The approval is the one thing that writes the memory, and the document is found there.
            Assert.NotNull(await context.Memory.ReadAsync(
                context.Project.Id, "conventions/format.md", CancellationToken.None));

            var rejected = await candidates.ProposeAsync(
                context.Project.Id,
                "TASK-1",
                "lessons/mistake.md",
                "# Mistake",
                "why",
                "cline",
                CancellationToken.None);
            var declined = await candidates.RejectAsync(
                context.Project.Id, "TASK-1", rejected.Id, "person", "not durable", CancellationToken.None);

            Assert.Equal(MemoryCandidateStatus.Rejected, declined.Status);
            // A rejection writes nothing: the document the person declined is not in the memory.
            Assert.Null(await context.Memory.ReadAsync(
                context.Project.Id, "lessons/mistake.md", CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_traversal_or_non_markdown_path_is_refused()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await SaveCardAsync(context, "TASK-1");
            var candidates = Store(context);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await candidates.ProposeAsync(
                    context.Project.Id, "TASK-1", "../escape.md", "# x", "why", "cline", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await candidates.ProposeAsync(
                    context.Project.Id, "TASK-1", "notes.txt", "# x", "why", "cline", CancellationToken.None));
        });
    }

    [Fact]
    public async Task A_candidate_is_decided_once()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            await SaveCardAsync(context, "TASK-1");
            var candidates = Store(context);

            var proposed = await candidates.ProposeAsync(
                context.Project.Id,
                "TASK-1",
                "decisions/auth.md",
                "# Auth",
                "why",
                "cline",
                CancellationToken.None);
            await candidates.ApproveAsync(
                context.Project.Id, "TASK-1", proposed.Id, "person", null, CancellationToken.None);

            // The second decision is refused rather than storing the document a second time.
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await candidates.ApproveAsync(
                    context.Project.Id, "TASK-1", proposed.Id, "person", null, CancellationToken.None));
        });
    }

    private static FileMemoryCandidateStore Store(TestContext context) =>
        new(context.Catalog, context.Cards, context.Memory);

    private static async Task SaveCardAsync(TestContext context, string cardId) =>
        await context.Cards.SaveAsync(
            InfrastructureSpecs.CreateCard(context.Project.Id, cardId, 1),
            0,
            CancellationToken.None);

    private static string CardDirectory(TestContext context, string cardId) =>
        FileCardStore.FindCardDirectory(context.Project.RootPath, cardId)
        ?? throw new InvalidOperationException($"Unknown card: {cardId}");
}
