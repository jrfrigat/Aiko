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
        new(context.Catalog, ReleaseStore(context), context.Events);

    /// <summary>The release history a plan is read against: a plan means nothing beside the releases it knows.</summary>
    private static FileReleaseStore ReleaseStore(TestContext context) =>
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

    /// <summary>
    /// A version that was recorded makes its plan a fact: reading it does not depend on somebody remembering
    /// to close the plan, and the reading leaves the person's document exactly as they wrote it.
    /// </summary>
    [Fact]
    public async Task A_recorded_version_reads_as_released_even_when_its_plan_was_never_closed()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"], IsCurrent: true),
                CancellationToken.None);

            var path = Path.Combine(context.StitchRoot, "release-plan.json");
            var asWritten = await File.ReadAllTextAsync(path);
            var recorded = await ReleaseStore(context).RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);

            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var plan = document.Find("v0.2.0")!;
            // The moment is the release's own, not the moment somebody read the plan.
            Assert.Equal(recorded.ReleasedAt, plan.ReleasedAt);
            // A released version is not the version new cards flow into: its work belongs to the next one.
            Assert.False(plan.IsCurrent);
            Assert.Null(document.Current);

            // Reading is not a moment to change the document: the file still holds the plan nobody closed, and
            // saying what shipped stays the person's statement to make.
            Assert.Equal(asWritten, await File.ReadAllTextAsync(path));
        });
    }

    /// <summary>
    /// A recorded version refuses a change whether or not its plan was ever closed, and a version recorded
    /// without a plan is not planned afterwards either.
    /// </summary>
    [Fact]
    public async Task A_recorded_versions_plan_refuses_a_change_without_being_closed()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var store = Store(context);
            var releases = ReleaseStore(context);
            await store.UpdateAsync(
                context.Project.Id,
                new ReleasePlanUpdate("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);
            await releases.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);
            await releases.RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.9.0", ReleaseSchemes.GitReleaseId, ["TASK-2"]),
                CancellationToken.None);

            // Nobody closed this plan and the change is refused anyway: the version is a fact.
            var unclosed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(
                    context.Project.Id,
                    new ReleasePlanUpdate("v0.2.0", AddCards: ["TASK-3"]),
                    CancellationToken.None).AsTask());
            Assert.Contains("closed", unclosed.Message, StringComparison.Ordinal);

            // v0.9.0 was released without ever being planned, and planning it now would be planning work for a
            // version that has already shipped.
            var neverPlanned = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(
                    context.Project.Id,
                    new ReleasePlanUpdate("v0.9.0", ReleaseSchemes.GitReleaseId, ["TASK-3"]),
                    CancellationToken.None).AsTask());
            Assert.Contains("recorded", neverPlanned.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Closing still closes: a plan that already reads as released can still be closed by hand, and what it
    /// records is the release's own moment, so the plan and the history agree about when the version shipped.
    /// </summary>
    [Fact]
    public async Task Closing_a_recorded_versions_plan_keeps_the_releases_own_moment()
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

            var recorded = await ReleaseStore(context).RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.2.0", ReleaseSchemes.GitReleaseId, ["TASK-1"]),
                CancellationToken.None);

            var closed = await store.CloseAsync(
                context.Project.Id,
                "v0.2.0",
                ["TASK-2"],
                carryIntoVersion: null,
                CancellationToken.None);

            Assert.Equal(recorded.ReleasedAt, closed.ReleasedAt);
            Assert.Equal(new[] { "TASK-1" }, closed.Cards.Select(entry => entry.CardId).ToArray());

            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            var carried = Assert.Single(document.Find("v0.3.0")!.Cards, entry => entry.CardId == "TASK-2");
            Assert.Equal("v0.2.0", carried.CarriedFromVersion);
        });
    }

    /// <summary>
    /// Work that outlived a version is not carried into a version that was already released: the only other
    /// plan is not a place for it, so it stays on the closed plan where a person can see it.
    /// </summary>
    [Fact]
    public async Task Work_is_never_carried_into_a_version_that_was_already_released()
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
                new ReleasePlanUpdate("v0.3.0", ReleaseSchemes.GitReleaseId, ["TASK-3"], IsCurrent: true),
                CancellationToken.None);

            // v0.3.0 is the most recently created plan nobody closed, and it is already released: a version
            // that shipped is not where the work of the next one goes.
            await ReleaseStore(context).RecordAsync(
                context.Project.Id,
                new RecordReleaseRequest("v0.3.0", ReleaseSchemes.GitReleaseId, ["TASK-3"]),
                CancellationToken.None);

            await store.CloseAsync(
                context.Project.Id,
                "v0.2.0",
                ["TASK-2"],
                carryIntoVersion: null,
                CancellationToken.None);

            var document = await store.ReadAsync(context.Project.Id, CancellationToken.None);
            Assert.Contains(document.Find("v0.2.0")!.Cards, entry => entry.CardId == "TASK-2");
            Assert.DoesNotContain(document.Find("v0.3.0")!.Cards, entry => entry.CardId == "TASK-2");
        });
    }
}
