using Aiko.Application.Contracts;
using Aiko.Infrastructure.Releases;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The release plans: what each version is waiting for, in the project's own file, and what the document
/// refuses to do.
/// </summary>
/// <remarks>
/// Held against a real project on disk rather than a stub, for the reason the release history is: the point
/// of the document is that it outlives the daemon that wrote it.
/// </remarks>
public sealed class ReleasePlanStoreSpecs
{
    private static FileReleasePlanStore Store(TestContext context) =>
        new(context.Catalog, context.Events);

    [Fact]
    public async Task A_plan_is_read_back_from_the_projects_own_file()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);

            // Nothing planned yet is a state of the project, not an error.
            var before = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            Assert.Empty(before.Plans);
            Assert.Null(before.Current);

            var plan = await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1", "TASK-2"], IsCurrent: true),
                CancellationToken.None);

            Assert.Equal("v0.2.0", plan.Version);
            Assert.Equal(ReleaseSchemes.GitReleaseId, plan.SchemeId);
            Assert.True(plan.IsCurrent);
            Assert.Null(plan.ReleasedAt);
            Assert.Equal(
                new[] { "TASK-1", "TASK-2" },
                plan.Cards.Select(entry => entry.CardId).ToArray());

            // A document beside the other documents, which is the point of it: it can be opened without Aiko,
            // and losing the database does not lose the answer to what a version was waiting for.
            var path = Path.Combine(context.StitchRoot, "release-plan.json");
            Assert.True(File.Exists(path), "The release plan should be a document in .aiko.");
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("schemaVersion", text, StringComparison.Ordinal);
            Assert.Contains("v0.2.0", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Two_changes_in_a_row_do_not_lose_each_other()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);

            // The second call is applied to the plan the first one stored, which is what the intent-shaped
            // update buys: a caller that read a document and wrote it back could drop the card added in
            // between. A card named twice joins once.
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", AddCards: ["TASK-2", "TASK-1"]),
                CancellationToken.None);

            var after = await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", RemoveCards: ["TASK-1"]),
                CancellationToken.None);
            Assert.Equal(new[] { "TASK-2" }, after.Cards.Select(entry => entry.CardId).ToArray());
        });
    }

    [Fact]
    public async Task Only_one_plan_is_the_one_new_cards_flow_into()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"], IsCurrent: true),
                CancellationToken.None);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.3.0", ReleaseSchemes.GitReleaseId, IsCurrent: true),
                CancellationToken.None);

            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            Assert.Equal("v0.3.0", document.Current?.Version);

            // The mark being single is what makes "where does a new card go" a question with one answer;
            // moving it is what changes that answer.
            Assert.False(document.Find("v0.2.0")!.IsCurrent);
            Assert.True(document.Find("v0.3.0")!.IsCurrent);
        });
    }

    [Fact]
    public async Task A_version_that_is_not_a_tag_is_refused()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
                store.UpdateAsync(
                    context.Project.Id,
                    new ReleasePlanUpdate("0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                    CancellationToken.None).AsTask());

            // The rule quoted is the release rule, because a plan is for a tag.
            Assert.Contains("git tag", refused.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_document_nobody_can_read_is_refused_as_one()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await File.WriteAllTextAsync(
                Path.Combine(context.StitchRoot, "release-plan.json"),
                "{ this is not a release plan document");

            // Reporting an empty plan document instead would claim the project plans nothing, which is a
            // different statement from "this file is broken".
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.ReadAsync(context.Project.Id, CancellationToken.None).AsTask());
        });
    }

    [Fact]
    public async Task Closing_a_version_moves_the_work_that_outlived_it()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1", "TASK-2"], IsCurrent: true),
                CancellationToken.None);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.3.0", ReleaseSchemes.GitReleaseId, ["TASK-3"]),
                CancellationToken.None);

            var closed = await store.CloseAsync(
                context.Project.Id,
                "v0.2.0",
                ["TASK-2"],
                carryIntoVersion: null,
                CancellationToken.None);

            Assert.NotNull(closed.ReleasedAt);
            Assert.False(closed.IsCurrent);
            Assert.Equal(new[] { "TASK-1" }, closed.Cards.Select(entry => entry.CardId).ToArray());

            // The card that outlived the version is in the next unreleased plan, and says where it came from:
            // a carry-over nobody can see is exactly the silent loss the story forbids.
            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var carried = Assert.Single(document.Find("v0.3.0")!.Cards, entry => entry.CardId == "TASK-2");
            Assert.Equal("v0.2.0", carried.CarriedFromVersion);
            Assert.Equal(2, document.Find("v0.3.0")!.Cards.Count);

            // Closing left the project with no plan new cards flow into, which is a state worth reading
            // rather than an error: the person has to say which version takes them now.
            Assert.Null(document.Current);
        });
    }

    [Fact]
    public async Task A_closed_plan_refuses_a_further_change()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);
            await store.CloseAsync(context.Project.Id, "v0.2.0", [], null, CancellationToken.None);

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(
                    context.Project.Id,
                    new ReleasePlanUpdate("v0.2.0", AddCards: ["TASK-2"]),
                    CancellationToken.None).AsTask());

            // The refusal names the reason, which is what lets an agent plan the work in another version
            // instead of insisting.
            Assert.Contains("closed", refused.Message, StringComparison.Ordinal);
        });
    }
}
