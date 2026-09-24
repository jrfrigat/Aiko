using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// A decision the project leaves to the user stays with the user. Under the "ask" policies an agent could
/// approve its own commit, resume the run that waited for approval, complete a stage that was waiting, or simply
/// switch the policy to "allow" - so "ask" meant "allow".
/// </summary>
/// <remarks>Each spec works in a project of its own whose policies ask for the user.</remarks>
public class UserDecisionSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    private const string AskingPolicies =
        "{\"workspaceMode\":\"Shared\",\"maxConcurrentRuns\":1,\"scopeOverlapPolicy\":\"Ask\"," +
        "\"sharedCheckoutCommitPolicy\":\"Ask\",\"sharedCheckoutPushPolicy\":\"Deny\",\"scopeExpansionPolicy\":\"Ask\"}";

    [Fact]
    public async Task No_agent_tool_approves_a_commit()
    {
        var (client, _) = await ConnectToAskingProjectAsync();
        await using var owned = client;

        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        Assert.DoesNotContain(tools, tool => tool.Name == "aiko_approve_commit");
    }

    [Fact]
    public async Task A_run_waiting_for_commit_approval_is_neither_resumed_nor_completed_by_the_agent()
    {
        var (client, _) = await ConnectToAskingProjectAsync();
        await using var owned = client;
        var executionId = await StartAsync(client);

        var reported = await CallAsync(
            client,
            "aiko_report_commit",
            new() { ["executionId"] = executionId, ["commitSha"] = "abc1234", ["message"] = "Change", ["files"] = new[] { "src/a.cs" } });
        Assert.NotEqual(true, reported.IsError);

        var resumed = await CallAsync(client, "aiko_resume_execution", new() { ["executionId"] = executionId, ["agentAdapterId"] = "claude-code" });
        Assert.True(resumed.IsError);
        Assert.Contains("user", Text(resumed), StringComparison.OrdinalIgnoreCase);

        var completed = await CallAsync(
            client,
            "aiko_complete_stage",
            new() { ["executionId"] = executionId, ["actualChangedFiles"] = Array.Empty<string>(), ["artifacts"] = Array.Empty<string>() });
        Assert.True(completed.IsError);
        Assert.Contains("waiting", Text(completed), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_run_waiting_for_a_scope_decision_is_not_resumed_by_the_agent()
    {
        var (client, _) = await ConnectToAskingProjectAsync();
        await using var owned = client;
        var executionId = await StartAsync(client);

        var requested = await CallAsync(
            client,
            "aiko_request_scope_expansion",
            new() { ["executionId"] = executionId, ["requestedScopeFiles"] = new[] { "docs/**" }, ["reason"] = "Docs" });
        Assert.NotEqual(true, requested.IsError);

        var resumed = await CallAsync(client, "aiko_resume_execution", new() { ["executionId"] = executionId, ["agentAdapterId"] = "claude-code" });
        Assert.True(resumed.IsError);
    }

    [Fact]
    public async Task An_agent_cannot_loosen_the_execution_policies()
    {
        var (client, projectId) = await ConnectToAskingProjectAsync();
        await using var owned = client;

        var loosened = await CallAsync(
            client,
            "aiko_update_settings",
            new() { ["projectId"] = projectId, ["settings"] = "{\"execution\":" + AskingPolicies.Replace("\"sharedCheckoutCommitPolicy\":\"Ask\"", "\"sharedCheckoutCommitPolicy\":\"Allow\"", StringComparison.Ordinal) + "}" });
        Assert.True(loosened.IsError);
        Assert.Contains("sharedCheckoutCommitPolicy", Text(loosened), StringComparison.Ordinal);

        // What is not a policy stays the agent's to change.
        var runs = await CallAsync(
            client,
            "aiko_update_settings",
            new() { ["projectId"] = projectId, ["settings"] = "{\"execution\":" + AskingPolicies.Replace("\"maxConcurrentRuns\":1", "\"maxConcurrentRuns\":2", StringComparison.Ordinal) + "}" });
        Assert.NotEqual(true, runs.IsError);
    }

    private static async Task<string> StartAsync(McpClient client)
    {
        var created = await CallAsync(client, "aiko_create_card", new() { ["kind"] = "task", ["title"] = "Decide", ["ownPriority"] = 1 });
        using var card = JsonDocument.Parse(Text(created));
        var cardId = card.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
        var started = await CallAsync(client, "aiko_start_stage", new() { ["cardId"] = cardId, ["stageId"] = "analysis", ["agentAdapterId"] = "claude-code" });
        Assert.NotEqual(true, started.IsError);
        using var execution = JsonDocument.Parse(Text(started));
        return execution.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments) =>
        await client.CallToolAsync(tool, arguments, cancellationToken: CancellationToken.None);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    private async Task<(McpClient Client, string ProjectId)> ConnectToAskingProjectAsync()
    {
        var name = $"decide-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.PostAsJsonAsync("/api/v1/projects/initialize", new { rootPath = root, name });
        response.EnsureSuccessStatusCode();
        var projectId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // The policies are set the way a person sets them: through the REST surface the Settings screen uses.
        using var settings = new StringContent(
            "{\"schemaVersion\":1,\"execution\":" + AskingPolicies + "}",
            System.Text.Encoding.UTF8,
            "application/json");
        using var written = await http.PutAsync($"api/v1/projects/{projectId}/settings", settings);
        written.EnsureSuccessStatusCode();

        var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{projectId}"),
                TransportMode = HttpTransportMode.StreamableHttp
            }),
            cancellationToken: CancellationToken.None);
        return (client, projectId);
    }
}
