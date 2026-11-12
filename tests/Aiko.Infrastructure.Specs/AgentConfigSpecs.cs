using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class AgentConfigSpecs
{
    [Fact]
    public async Task Claude_code_defaults_to_acp_with_its_launcher()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            var config = await service.GetEffectiveAsync(null, "claude-code", CancellationToken.None);

            Assert.Equal(AgentTransport.Acp, config.Transport);
            Assert.Contains(AgentTransport.Command, config.SupportedTransports);
            Assert.Contains(AgentTransport.Acp, config.SupportedTransports);
            Assert.Equal("npx", config.Acp!.Executable);
            Assert.Contains("@zed-industries/claude-code-acp", config.Acp.Args!);
            Assert.Contains("mcp", config.Capabilities.AikoAccess!);
            Assert.Equal(AgentConfigSource.Default, config.Sources["transport"]);
            Assert.True(config.CanDispatchDirectly);
        });
    }

    [Fact]
    public async Task User_and_project_layers_merge_in_that_order()
    {
        await WithStoreAsync(async (service, store, project) =>
        {
            await service.SaveUserAsync(
                new AgentConfig(
                    Id: "claude-code",
                    Transport: AgentTransport.Command,
                    Priority: 5),
                CancellationToken.None);

            await service.SaveProjectAsync(
                project.Id,
                new AgentConfig(
                    Id: "claude-code",
                    Transport: AgentTransport.Acp,
                    Capabilities: new AgentCapabilityProfile(Actions: ["start"])),
                CancellationToken.None);

            var config = await service.GetEffectiveAsync(
                project.Id,
                "claude-code",
                CancellationToken.None);

            Assert.Equal(AgentTransport.Acp, config.Transport);
            Assert.Equal(AgentConfigSource.Project, config.Sources["transport"]);
            Assert.Equal(5, config.Priority);
            Assert.Equal(AgentConfigSource.User, config.Sources["priority"]);
            Assert.Equal(["start"], config.Capabilities.Actions);
            Assert.Equal(AgentConfigSource.Project, config.Sources["capabilities.actions"]);
            Assert.Contains("mcp", config.Capabilities.AikoAccess!);
            Assert.Equal(
                AgentConfigSource.Default,
                config.Sources["capabilities.aikoAccess"]);

            var read = await store.ReadUserAsync("claude-code", CancellationToken.None);
            Assert.Equal(5, read!.Priority);
        });
    }

    [Fact]
    public async Task An_unsupported_transport_is_refused()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.SaveUserAsync(
                    new AgentConfig(Id: "claude-code", Transport: AgentTransport.Sdk),
                    CancellationToken.None));

            Assert.Contains("Sdk", refused.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Acp_without_an_executable_is_refused()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.SaveUserAsync(
                    new AgentConfig(
                        Id: "claude-code",
                        Transport: AgentTransport.Acp,
                        Acp: new AgentAcpConfig(Executable: null)),
                    CancellationToken.None));

            Assert.Contains("executable", refused.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Unknown_fields_survive_a_round_trip()
    {
        await WithStoreAsync(async (_, store, _) =>
        {
            var directory = Path.Combine(Path.GetDirectoryName(store.Paths.DatabasePath)!, "agents");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "claude-code.json");
            await File.WriteAllTextAsync(
                path,
                """{ "id": "claude-code", "futureField": { "nested": 42 } }""");

            var read = await store.ReadUserAsync("claude-code", CancellationToken.None);
            Assert.NotNull(read);
            Assert.True(read.ExtensionData!.ContainsKey("futureField"));

            await store.SaveUserAsync(read, CancellationToken.None);

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("futureField", written, StringComparison.Ordinal);
            Assert.Contains("nested", written, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_path_like_adapter_id_is_refused()
    {
        await WithStoreAsync(async (_, store, _) =>
        {
            var refused = Assert.Throws<ArgumentException>(() =>
            {
                _ = store.ReadUserAsync("../outside", CancellationToken.None);
            });

            Assert.Contains("agent id", refused.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Project_configuration_lives_under_the_project_data_root()
    {
        await WithStoreAsync(async (service, _, project) =>
        {
            await service.SaveProjectAsync(
                project.Id,
                new AgentConfig(Id: "claude-code", Enabled: false),
                CancellationToken.None);

            var path = Path.Combine(
                AikoProjectPaths.DataRoot(project.RootPath),
                "agents",
                "claude-code.json");
            Assert.True(File.Exists(path));

            var config = await service.GetEffectiveAsync(
                project.Id,
                "claude-code",
                CancellationToken.None);
            Assert.False(config.Enabled);
            Assert.Equal(AgentConfigSource.Project, config.Sources["enabled"]);
        });
    }

    [Fact]
    public async Task An_unknown_adapter_is_refused()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.GetEffectiveAsync(null, "not-an-agent", CancellationToken.None));
        });
    }

    private static async Task WithStoreAsync(
        Func<AgentConfigService, InspectableStore, RegisteredProject, Task> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Aiko.Specs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectRoot = Path.Combine(root, "project");
            Directory.CreateDirectory(projectRoot);
            var project = new RegisteredProject("project-1", "Project", projectRoot, "project");
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
            var catalog = new SingleProjectCatalog(project);
            var store = new InspectableStore(
                new FileAgentConfigStore(catalog, paths),
                paths);
            var service = new AgentConfigService(AgentAdapters.CreateBuiltIn(), store);
            await action(service, store, project);
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    private sealed class InspectableStore(
        IAgentConfigStore inner,
        AikoDataPaths paths) : IAgentConfigStore
    {
        public AikoDataPaths Paths => paths;

        public ValueTask<AgentConfig?> ReadUserAsync(
            string adapterId,
            CancellationToken cancellationToken) =>
            inner.ReadUserAsync(adapterId, cancellationToken);

        public ValueTask<AgentConfig?> ReadProjectAsync(
            string projectId,
            string adapterId,
            CancellationToken cancellationToken) =>
            inner.ReadProjectAsync(projectId, adapterId, cancellationToken);

        public ValueTask SaveUserAsync(AgentConfig config, CancellationToken cancellationToken) =>
            inner.SaveUserAsync(config, cancellationToken);

        public ValueTask SaveProjectAsync(
            string projectId,
            AgentConfig config,
            CancellationToken cancellationToken) =>
            inner.SaveProjectAsync(projectId, config, cancellationToken);

        public ValueTask<IReadOnlyList<string>> ListUserAsync(CancellationToken cancellationToken) =>
            inner.ListUserAsync(cancellationToken);

        public ValueTask<IReadOnlyList<string>> ListProjectAsync(
            string projectId,
            CancellationToken cancellationToken) =>
            inner.ListProjectAsync(projectId, cancellationToken);
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
                string.Equals(projectIdOrSlug, project.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectIdOrSlug, project.Slug, StringComparison.OrdinalIgnoreCase)
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
