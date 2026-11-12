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
            Assert.Equal("claude-code", config.AdapterId);
            Assert.Equal("Claude Code", config.DisplayName);
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
                        Acp: new AgentAcpConfig(Executable: string.Empty)),
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

    [Fact]
    public async Task An_instance_uses_its_adapter_defaults_and_environment()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            await service.SaveUserAsync(
                new AgentConfig(
                    Id: "claude-code-deepseek",
                    AdapterId: "claude-code",
                    DisplayName: "Claude Code (DeepSeek)",
                    Transport: AgentTransport.Acp,
                    Acp: new AgentAcpConfig(
                        Executable: "npx",
                        Args: ["-y", "@zed-industries/claude-code-acp"],
                        Environment: new Dictionary<string, string>
                        {
                            ["ANTHROPIC_BASE_URL"] = "https://deepseek",
                            ["ANTHROPIC_MODEL"] = "deepseek-chat"
                        })),
                CancellationToken.None);

            var config = await service.GetEffectiveAsync(
                null,
                "claude-code-deepseek",
                CancellationToken.None);

            Assert.Equal("claude-code", config.AdapterId);
            Assert.Equal("Claude Code (DeepSeek)", config.DisplayName);
            Assert.Equal(AgentTransport.Acp, config.Transport);
            Assert.Equal("npx", config.Acp!.Executable);
            Assert.Equal(
                "https://deepseek",
                config.Acp.Environment!["ANTHROPIC_BASE_URL"]);
            Assert.Contains("mcp", config.Capabilities.AikoAccess!);
        });
    }

    [Fact]
    public async Task Environment_values_are_merged_by_key_and_the_sentinel_inherits()
    {
        await WithStoreAsync(async (service, _, project) =>
        {
            await service.SaveUserAsync(
                new AgentConfig(
                    Id: "claude-code-deepseek",
                    AdapterId: "claude-code",
                    Acp: new AgentAcpConfig(
                        Executable: "npx",
                        Environment: new Dictionary<string, string>
                        {
                            ["A"] = "1",
                            ["B"] = "2"
                        })),
                CancellationToken.None);
            await service.SaveProjectAsync(
                project.Id,
                new AgentConfig(
                    Id: "claude-code-deepseek",
                    AdapterId: "claude-code",
                    Acp: new AgentAcpConfig(
                        Executable: "npx",
                        Environment: new Dictionary<string, string>
                        {
                            ["A"] = AgentConfig.SecretSentinel,
                            ["B"] = "3",
                            ["C"] = "4"
                        })),
                CancellationToken.None);

            var config = await service.GetEffectiveAsync(
                project.Id,
                "claude-code-deepseek",
                CancellationToken.None);

            Assert.Equal("1", config.Acp!.Environment!["A"]);
            Assert.Equal("3", config.Acp.Environment!["B"]);
            Assert.Equal("4", config.Acp.Environment!["C"]);
        });
    }

    [Fact]
    public void Masking_hides_every_environment_value()
    {
        var config = new EffectiveAgentConfig(
            "claude-code-deepseek",
            true,
            AgentTransport.Acp,
            AgentTransport.Command,
            0,
            AgentCapabilityProfile.Default,
            new AgentAcpConfig(
                Executable: "npx",
                Environment: new Dictionary<string, string> { ["ANTHROPIC_AUTH_TOKEN"] = "secret" }),
            new AgentSdkConfig(
                "sdk",
                new Dictionary<string, string> { ["SDK_KEY"] = "value" }),
            true,
            [AgentTransport.Command, AgentTransport.Acp],
            new Dictionary<string, AgentConfigSource>());

        var masked = AgentSecretMasking.Mask(config);

        Assert.Equal(AgentConfig.SecretSentinel, masked.Acp!.Environment!["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal(AgentConfig.SecretSentinel, masked.Sdk!.Environment!["SDK_KEY"]);
        Assert.Equal("secret", config.Acp!.Environment!["ANTHROPIC_AUTH_TOKEN"]);
    }

    [Fact]
    public async Task A_sentinel_keeps_the_stored_environment_value()
    {
        await WithStoreAsync(async (service, store, _) =>
        {
            await service.SaveUserAsync(
                new AgentConfig(
                    Id: "claude-code-deepseek",
                    AdapterId: "claude-code",
                    Acp: new AgentAcpConfig(
                        Executable: "npx",
                        Environment: new Dictionary<string, string>
                        {
                            ["ANTHROPIC_AUTH_TOKEN"] = "secret",
                            ["ANTHROPIC_MODEL"] = "old"
                        })),
                CancellationToken.None);
            var stored = await store.ReadUserAsync("claude-code-deepseek", CancellationToken.None);
            var incoming = new AgentConfig(
                Id: "claude-code-deepseek",
                AdapterId: "claude-code",
                Acp: new AgentAcpConfig(
                    Executable: "npx",
                    Environment: new Dictionary<string, string>
                    {
                        ["ANTHROPIC_AUTH_TOKEN"] = AgentConfig.SecretSentinel,
                        ["ANTHROPIC_MODEL"] = "new",
                        ["NEW_TOKEN"] = AgentConfig.SecretSentinel
                    }));

            await service.SaveUserAsync(
                AgentSecretMasking.KeepStoredValues(stored, incoming),
                CancellationToken.None);

            var saved = await store.ReadUserAsync("claude-code-deepseek", CancellationToken.None);
            Assert.Equal("secret", saved!.Acp!.Environment!["ANTHROPIC_AUTH_TOKEN"]);
            Assert.Equal("new", saved.Acp.Environment!["ANTHROPIC_MODEL"]);
            Assert.DoesNotContain("NEW_TOKEN", saved.Acp.Environment!.Keys);
        });
    }

    [Fact]
    public async Task An_instance_without_an_adapter_is_refused()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            var refused = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.SaveUserAsync(
                    new AgentConfig(Id: "my-agent"),
                    CancellationToken.None));

            Assert.Contains("AdapterId", refused.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ListEffective_includes_the_default_and_extra_instances()
    {
        await WithStoreAsync(async (service, _, _) =>
        {
            await service.SaveUserAsync(
                new AgentConfig(
                    Id: "claude-code-deepseek",
                    AdapterId: "claude-code"),
                CancellationToken.None);

            var configs = await service.ListEffectiveAsync(null, CancellationToken.None);

            Assert.Contains(
                configs,
                config => config.Id == "claude-code" &&
                    config.AdapterId == "claude-code");
            Assert.Contains(
                configs,
                config => config.Id == "claude-code-deepseek" &&
                    config.AdapterId == "claude-code");
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
