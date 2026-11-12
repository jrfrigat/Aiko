using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Storage;
using Capastrum.Acp.Protocol;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class CapastrumAcpTransportSpecs
{
    [Fact]
    public async Task The_transport_launches_the_agent_and_passes_the_aiko_mcp_server()
    {
        await WithContextAsync(async (context, factory, deliveryStore, root) =>
        {
            var transport = Create(context, factory, deliveryStore, root);

            var result = await transport.DeliverAsync(context, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("session-1", result.SessionId);
            Assert.Equal("working", result.Message);
            Assert.Equal("npx", factory.Options!.Executable);
            Assert.Contains("@zed-industries/claude-code-acp", factory.Options.Arguments);
            Assert.Equal(Path.GetFullPath(root), factory.Options.WorkspaceRoot);

            var server = Assert.Single(factory.Session.Servers!);
            Assert.Equal("aiko", server.Name);
            Assert.Equal("http", server.Type);
            Assert.Equal("http://127.0.0.1:24598/mcp/projects/project", server.Url);
            Assert.Equal("Bearer token", Assert.Single(server.Headers!).Value);

            Assert.Contains("Aiko command CMD-1", factory.Session.Prompt, StringComparison.Ordinal);
            Assert.Contains("implementation", factory.Session.Prompt, StringComparison.Ordinal);
            Assert.NotNull(deliveryStore.Updated);
            Assert.Equal("session-1", deliveryStore.Updated!.SessionId);
            Assert.Equal(AgentDeliveryState.Running, deliveryStore.Updated.State);
        });
    }

    [Fact]
    public async Task The_instance_environment_reaches_the_launch_without_reaching_the_prompt()
    {
        var environment = new Dictionary<string, string>
        {
            ["ANTHROPIC_BASE_URL"] = "https://deepseek.example",
            ["ANTHROPIC_AUTH_TOKEN"] = "secret-value"
        };

        await WithContextAsync(async (context, factory, deliveryStore, root) =>
        {
            var transport = Create(context, factory, deliveryStore, root);

            var result = await transport.DeliverAsync(context, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("https://deepseek.example", factory.Options!.Environment!["ANTHROPIC_BASE_URL"]);
            Assert.Equal("secret-value", factory.Options.Environment!["ANTHROPIC_AUTH_TOKEN"]);

            var server = Assert.Single(factory.Session.Servers!);
            Assert.DoesNotContain("secret-value", factory.Session.Prompt!, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-value", result.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-value", server.Url!, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "secret-value",
                Assert.Single(server.Headers!).Value!,
                StringComparison.Ordinal);
        }, environment);
    }

    [Fact]
    public async Task Two_instances_of_one_adapter_launch_with_their_own_environment()
    {
        await WithRootAsync(async root =>
        {
            var factory = new FakeFactory();
            var deliveries = new FakeDeliveryStore();
            var first = CreateContext(
                root,
                new Dictionary<string, string> { ["ANTHROPIC_MODEL"] = "deepseek-chat" });
            var second = CreateContext(
                root,
                new Dictionary<string, string> { ["ANTHROPIC_MODEL"] = "deepseek-reasoner" });
            var transport = Create(first, factory, deliveries, root);

            await transport.DeliverAsync(first, CancellationToken.None);
            Assert.Equal("deepseek-chat", factory.Options!.Environment!["ANTHROPIC_MODEL"]);

            await transport.DeliverAsync(second, CancellationToken.None);
            Assert.Equal("deepseek-reasoner", factory.Options!.Environment!["ANTHROPIC_MODEL"]);
        });
    }

    [Fact]
    public async Task A_cancelled_turn_is_a_failure()
    {
        await WithContextAsync(async (context, factory, deliveryStore, _) =>
        {
            factory.Session.StopReason = AcpStopReason.Cancelled;
            var transport = Create(context, factory, deliveryStore, _);

            var result = await transport.DeliverAsync(context, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("Cancelled", result.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task File_access_outside_the_project_is_refused()
    {
        await WithContextAsync(async (context, factory, deliveryStore, root) =>
        {
            var transport = Create(context, factory, deliveryStore, root);
            await transport.DeliverAsync(context, CancellationToken.None);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                _ = await factory.Options!.ReadTextFile(
                    Path.Combine(root, "..", "outside.txt"),
                    CancellationToken.None);
            });
        });
    }

    [Fact]
    public async Task File_access_inside_the_project_is_allowed()
    {
        await WithContextAsync(async (context, factory, deliveryStore, root) =>
        {
            var transport = Create(context, factory, deliveryStore, root);
            await transport.DeliverAsync(context, CancellationToken.None);
            var path = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(path, "hello");

            var content = await factory.Options!.ReadTextFile(path, CancellationToken.None);

            Assert.Equal("hello", content);
        });
    }

    private static CapastrumAcpAgentTransport Create(
        AgentDeliveryContext context,
        FakeFactory factory,
        FakeDeliveryStore deliveries,
        string root) => new(
        new FakeProjects(new RegisteredProject(
            "project-1",
            "Project",
            root,
            "project")),
        new FakeMcpEndpoint(),
        deliveries,
        factory);

    private static Task WithContextAsync(
        Func<AgentDeliveryContext, FakeFactory, FakeDeliveryStore, string, Task> action,
        IReadOnlyDictionary<string, string>? environment = null) =>
        WithRootAsync(async root =>
            await action(CreateContext(root, environment), new FakeFactory(), new FakeDeliveryStore(), root));

    private static async Task WithRootAsync(Func<string, Task> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Aiko.Specs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await action(root);
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    private static AgentDeliveryContext CreateContext(
        string root,
        IReadOnlyDictionary<string, string>? environment = null) => new(
        "project-1",
        new CardCommand(
            "CMD-1",
            "TASK-1",
            CardCommandAction.Start,
            "implementation",
            null,
            "claude-code",
            null,
            CardCommandState.Queued,
            "you",
            DateTimeOffset.UtcNow,
            null,
            null,
            null),
        new AgentDelivery(
            "DLV-1",
            "CMD-1",
            "claude-code",
            AgentTransport.Acp,
            AgentDeliveryState.Starting,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null),
        new EffectiveAgentConfig(
            "claude-code",
            true,
            AgentTransport.Acp,
            AgentTransport.Command,
            0,
            AgentCapabilityProfile.Default,
            new AgentAcpConfig(
                "npx",
                ["-y", "@zed-industries/claude-code-acp"],
                Environment: environment),
            null,
            true,
            [AgentTransport.Command, AgentTransport.Acp],
            new Dictionary<string, AgentConfigSource>()));

    private sealed class FakeFactory : IAcpClientFactory
    {
        public AcpLaunchOptions? Options { get; private set; }

        public FakeSession Session { get; } = new();

        public Task<IAcpClientSession> StartAsync(
            AcpLaunchOptions options,
            CancellationToken cancellationToken)
        {
            Options = options;
            Session.OnText = options.OnText;
            return Task.FromResult<IAcpClientSession>(Session);
        }
    }

    private sealed class FakeSession : IAcpClientSession
    {
        public Action<string>? OnText { get; set; }

        public IReadOnlyList<AcpMcpServer>? Servers { get; private set; }

        public string? Prompt { get; private set; }

        public AcpStopReason StopReason { get; set; } = AcpStopReason.EndTurn;

        public ValueTask<string> NewSessionAsync(
            IReadOnlyList<AcpMcpServer> mcpServers,
            CancellationToken cancellationToken)
        {
            Servers = mcpServers;
            return ValueTask.FromResult("session-1");
        }

        public ValueTask<AcpPromptResponse> SendPromptAsync(
            string sessionId,
            string prompt,
            CancellationToken cancellationToken)
        {
            Prompt = prompt;
            OnText?.Invoke("working");
            return ValueTask.FromResult(new AcpPromptResponse { StopReason = StopReason });
        }

        public ValueTask CloseSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMcpEndpoint : IAgentMcpEndpoint
    {
        public ValueTask<AgentMcpEndpoint> GetAsync(
            string projectId,
            string deliveryId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentMcpEndpoint(
                "http://127.0.0.1:24598/mcp/projects/project",
                "token"));

        public ValueTask RevokeAsync(
            AgentMcpEndpoint endpoint,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeProjects(RegisteredProject project) : IProjectCatalog
    {
        public ValueTask<IReadOnlyList<RegisteredProject>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RegisteredProject>>([project]);

        public ValueTask<RegisteredProject?> FindAsync(
            string projectIdOrSlug,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<RegisteredProject?>(project);

        public ValueTask<bool> IsSlugTakenAsync(
            string slug,
            string? exceptProjectId,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask SaveAsync(
            RegisteredProject project,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<bool> RemoveAsync(
            string projectId,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class FakeDeliveryStore : IAgentDeliveryStore
    {
        public AgentDelivery? Updated { get; private set; }

        public ValueTask<IReadOnlyList<AgentDelivery>> ListAsync(
            string projectId,
            bool includeClosed,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AgentDelivery>>([]);

        public ValueTask<AgentDelivery?> FindAsync(
            string projectId,
            string deliveryId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDelivery?>(Updated);

        public ValueTask<AgentDelivery?> FindLatestForCommandAsync(
            string projectId,
            string commandId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDelivery?>(Updated);

        public ValueTask<AgentDelivery> CreateAsync(
            string projectId,
            string commandId,
            string agentInstanceId,
            string? adapterId,
            AgentTransport transport,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentDelivery> UpdateAsync(
            string projectId,
            AgentDelivery delivery,
            CancellationToken cancellationToken)
        {
            Updated = delivery;
            return ValueTask.FromResult(delivery);
        }
    }
}
