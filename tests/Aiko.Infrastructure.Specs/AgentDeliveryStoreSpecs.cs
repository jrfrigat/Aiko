using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public sealed class AgentDeliveryStoreSpecs
{
    [Fact]
    public async Task An_old_delivery_without_instance_fields_reads_as_its_adapter()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var directory = AikoProjectPaths.DataRoot(root);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "deliveries.json"),
                """
                {
                  "schemaVersion": 1,
                  "nextSequence": 2,
                  "entries": [
                    {
                      "id": "DLV-1",
                      "commandId": "CMD-1",
                      "agentAdapterId": "claude-code",
                      "transport": "Acp",
                      "state": "Completed",
                      "sessionId": "session-1",
                      "executionId": null,
                      "failureReason": null,
                      "createdAtUtc": "2026-11-01T10:00:00+00:00",
                      "startedAtUtc": "2026-11-01T10:00:01+00:00",
                      "finishedAtUtc": "2026-11-01T10:05:00+00:00"
                    }
                  ]
                }
                """);

            var delivery = Assert.Single(
                await store.ListAsync("project-1", includeClosed: true, CancellationToken.None));

            Assert.Null(delivery.AgentInstanceId);
            Assert.Null(delivery.AdapterId);
            Assert.Equal("claude-code", delivery.ResolvedInstanceId);
            Assert.Equal("claude-code", delivery.ResolvedAdapterId);
        });
    }

    [Fact]
    public async Task A_new_delivery_records_its_instance_and_adapter()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var created = await store.CreateAsync(
                "project-1",
                "CMD-1",
                "claude-code-deepseek",
                "claude-code",
                AgentTransport.Acp,
                CancellationToken.None);

            Assert.Equal("claude-code-deepseek", created.AgentInstanceId);
            Assert.Equal("claude-code", created.AdapterId);
            Assert.Equal("claude-code-deepseek", created.ResolvedInstanceId);
            Assert.Equal("claude-code", created.ResolvedAdapterId);
        });
    }

    [Fact]
    public async Task A_recorded_delivery_survives_a_reading_store()
    {
        await WithStoreAsync(async (_, root) =>
        {
            var store = new FileAgentDeliveryStore(new SingleProjectCatalog(
                new RegisteredProject("project-1", "Project", root, "project")));
            await store.CreateAsync(
                "project-1",
                "CMD-1",
                "claude-code-deepseek",
                "claude-code",
                AgentTransport.Acp,
                CancellationToken.None);

            var delivery = Assert.Single(
                await store.ListAsync("project-1", includeClosed: true, CancellationToken.None));

            Assert.Equal("claude-code-deepseek", delivery.AgentInstanceId);
            Assert.Equal("claude-code", delivery.AdapterId);
        });
    }

    private static async Task WithStoreAsync(
        Func<FileAgentDeliveryStore, string, Task> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Aiko.Specs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new RegisteredProject("project-1", "Project", root, "project");
            await action(new FileAgentDeliveryStore(new SingleProjectCatalog(project)), root);
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    private sealed class SingleProjectCatalog(RegisteredProject project) : IProjectCatalog
    {
        public ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RegisteredProject>>([project]);

        public ValueTask<RegisteredProject?> FindAsync(
            string projectIdOrSlug,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<RegisteredProject?>(
                StringComparer.OrdinalIgnoreCase.Equals(projectIdOrSlug, project.Id) ||
                StringComparer.OrdinalIgnoreCase.Equals(projectIdOrSlug, project.Slug)
                    ? project
                    : null);

        public ValueTask<bool> IsSlugTakenAsync(
            string slug,
            string? exceptProjectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask SaveAsync(
            RegisteredProject project,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> RemoveAsync(
            string projectId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }
}
