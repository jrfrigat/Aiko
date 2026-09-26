using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Projects;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// OpenCode's configuration: the flat <c>mcp</c> object in the workspace's <c>opencode.json</c> and the
/// global <c>~/.config/opencode/opencode.json</c>, where each project gets its own key. The adapter is
/// exercised through the unified installer, so what is checked is the files a person ends up with.
/// </summary>
public sealed class OpenCodeAdapterSpecs
{
    [Fact]
    public void OpenCode_is_one_of_the_adapters_Aiko_ships()
    {
        Assert.Contains(AgentAdapters.CreateBuiltIn(), adapter => adapter.Id == "opencode");
    }

    [Fact]
    public async Task OpenCode_keeps_a_foreign_server_in_the_project_file_through_apply_and_remove()
    {
        await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
        {
            var path = Path.Combine(context.Project.RootPath, "opencode.json");
            await File.WriteAllTextAsync(
                path,
                """{"mcp":{"other":{"type":"local","command":["other"]}}}""");

            var installer = new UnifiedAgentInstaller(
                [new OpenCodeAgentAdapter()],
                context.Catalog,
                new FileProjectDefinitionStore(context.Catalog));
            await installer.ApplyAsync(
                context.Project.Id,
                $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}",
                "test-token",
                ["opencode"],
                CancellationToken.None);
            await installer.UninstallAsync(context.Project.Id, ["opencode"], CancellationToken.None);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var servers = document.RootElement.GetProperty("mcp");
            Assert.False(servers.TryGetProperty("aiko", out _));
            Assert.True(servers.TryGetProperty("other", out _));
        });
    }

    [Fact]
    public async Task OpenCode_project_and_global_configuration_carry_the_mcp_entry_and_the_token()
    {
        var home = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var originalHome = Environment.GetEnvironmentVariable("AIKO_USER_HOME");
        try
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", home);
            await InfrastructureSpecs.WithInitializedProjectAsync(async context =>
            {
                var installer = new UnifiedAgentInstaller(
                    [new OpenCodeAgentAdapter()],
                    context.Catalog,
                    new FileProjectDefinitionStore(context.Catalog));
                var endpoint = $"http://127.0.0.1:18471/mcp/projects/{context.Project.Id}";
                var applied = await installer.ApplyAsync(
                    context.Project.Id, endpoint, "test-token", ["opencode"], CancellationToken.None);
                Assert.All(applied.AdapterResults, result => Assert.True(result.Succeeded));

                // The workspace file: the entry OpenCode reads for this project.
                var projectConfig = Path.Combine(context.Project.RootPath, "opencode.json");
                using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(projectConfig)))
                {
                    var server = document.RootElement.GetProperty("mcp").GetProperty("aiko");
                    Assert.Equal("remote", server.GetProperty("type").GetString());
                    Assert.Equal(endpoint, server.GetProperty("url").GetString());
                    Assert.Equal(
                        "Bearer test-token",
                        server.GetProperty("headers").GetProperty("Authorization").GetString());
                }

                // A global file cannot name a project, so the entry is keyed by the project's handle and
                // several projects can coexist.
                var globalConfig = Path.Combine(home, ".config", "opencode", "opencode.json");
                using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(globalConfig)))
                {
                    var servers = document.RootElement.GetProperty("mcp");
                    Assert.True(servers.TryGetProperty($"aiko-{context.Project.Handle}", out _));
                }

                // The workspace carries the procedure and the contract.
                Assert.True(File.Exists(
                    Path.Combine(context.Project.RootPath, ".opencode", "commands", "aiko-run.md")));
                var agents = await File.ReadAllTextAsync(Path.Combine(context.Project.RootPath, "AGENTS.md"));
                Assert.Contains("aiko:begin", agents, StringComparison.Ordinal);

                // Running again changes nothing.
                var again = await installer.ApplyAsync(
                    context.Project.Id, endpoint, "test-token", ["opencode"], CancellationToken.None);
                Assert.All(
                    again.AdapterResults.SelectMany(result => result.Files),
                    file => Assert.Equal(InstallationFileStatus.Unchanged, file.Status));

                var removed = await installer.UninstallAsync(
                    context.Project.Id, ["opencode"], CancellationToken.None);
                Assert.All(removed.AdapterResults, result => Assert.True(result.Succeeded));

                using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(projectConfig)))
                {
                    Assert.False(document.RootElement.GetProperty("mcp").TryGetProperty("aiko", out _));
                }

                var afterRemoval = await File.ReadAllTextAsync(
                    Path.Combine(context.Project.RootPath, "AGENTS.md"));
                Assert.DoesNotContain("aiko:begin", afterRemoval, StringComparison.Ordinal);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIKO_USER_HOME", originalHome);
            try
            {
                Directory.Delete(home, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a spec failure.
            }
        }
    }
}
