using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The agent-configuration API: every configured instance is listed by its own id, environment values leave
/// the daemon masked, and the sentinel a caller sends back keeps the secret that is stored for that layer.
/// </summary>
public class AgentConfigApiSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    [Fact]
    public async Task The_user_list_returns_the_default_and_extra_instances()
    {
        var (http, _, _) = await NewProjectAsync();
        using var owned = http;

        using var put = await http.PutAsJsonAsync(
            "/api/v1/agent-configs/claude-code-deepseek",
            new { adapterId = "claude-code", displayName = "Claude Code (DeepSeek)" });
        put.EnsureSuccessStatusCode();

        using var list = await http.GetAsync("/api/v1/agent-configs");
        list.EnsureSuccessStatusCode();
        var configs = await ReadConfigsAsync(list);

        Assert.Contains("claude-code", configs.Keys);
        Assert.Contains("claude-code-deepseek", configs.Keys);
        Assert.Equal(
            "claude-code",
            configs["claude-code-deepseek"].GetProperty("adapterId").GetString());
    }

    [Fact]
    public async Task The_project_list_returns_the_default_and_extra_instances()
    {
        var (http, projectId, _) = await NewProjectAsync();
        using var owned = http;

        using var put = await http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/agent-configs/claude-code-deepseek",
            new { adapterId = "claude-code" });
        put.EnsureSuccessStatusCode();

        using var list = await http.GetAsync($"/api/v1/projects/{projectId}/agent-configs");
        list.EnsureSuccessStatusCode();
        var configs = await ReadConfigsAsync(list);

        Assert.Contains("claude-code", configs.Keys);
        Assert.Contains("claude-code-deepseek", configs.Keys);
    }

    [Fact]
    public async Task A_secret_is_masked_in_the_answer_and_stored()
    {
        var (http, projectId, root) = await NewProjectAsync();
        using var owned = http;

        using var put = await http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/agent-configs/claude-code-deepseek",
            new
            {
                adapterId = "claude-code",
                acp = new
                {
                    executable = "npx",
                    args = new[] { "-y", "@zed-industries/claude-code-acp" },
                    environment = new Dictionary<string, string>
                    {
                        ["ANTHROPIC_AUTH_TOKEN"] = "secret-value",
                        ["ANTHROPIC_MODEL"] = "deepseek-chat"
                    }
                }
            });
        put.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        var environment = answer.RootElement.GetProperty("acp").GetProperty("environment");
        Assert.Equal("***", environment.GetProperty("ANTHROPIC_AUTH_TOKEN").GetString());
        Assert.Equal("***", environment.GetProperty("ANTHROPIC_MODEL").GetString());

        var path = Path.Combine(root, ".aiko", "agents", "claude-code-deepseek.json");
        Assert.Contains("secret-value", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_masked_value_sent_back_keeps_the_stored_secret()
    {
        var (http, projectId, root) = await NewProjectAsync();
        using var owned = http;
        var url = $"/api/v1/projects/{projectId}/agent-configs/claude-code-deepseek";

        using var first = await http.PutAsJsonAsync(
            url,
            new
            {
                adapterId = "claude-code",
                acp = new
                {
                    executable = "npx",
                    environment = new Dictionary<string, string>
                    {
                        ["ANTHROPIC_AUTH_TOKEN"] = "secret-value",
                        ["ANTHROPIC_MODEL"] = "deepseek-chat"
                    }
                }
            });
        first.EnsureSuccessStatusCode();

        using var second = await http.PutAsJsonAsync(
            url,
            new
            {
                adapterId = "claude-code",
                acp = new
                {
                    executable = "npx",
                    environment = new Dictionary<string, string>
                    {
                        ["ANTHROPIC_AUTH_TOKEN"] = "***",
                        ["ANTHROPIC_MODEL"] = "deepseek-reasoner"
                    }
                }
            });
        second.EnsureSuccessStatusCode();

        var path = Path.Combine(root, ".aiko", "agents", "claude-code-deepseek.json");
        using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var environment = stored.RootElement.GetProperty("acp").GetProperty("environment");
        Assert.Equal("secret-value", environment.GetProperty("ANTHROPIC_AUTH_TOKEN").GetString());
        Assert.Equal("deepseek-reasoner", environment.GetProperty("ANTHROPIC_MODEL").GetString());
    }

    [Fact]
    public async Task The_adapter_id_path_edits_its_default_instance()
    {
        var (http, projectId, _) = await NewProjectAsync();
        using var owned = http;

        using var put = await http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/agent-configs/claude-code",
            new { displayName = "Claude Code" });
        put.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await put.Content.ReadAsStringAsync());

        Assert.Equal("claude-code", answer.RootElement.GetProperty("id").GetString());
        Assert.Equal("claude-code", answer.RootElement.GetProperty("adapterId").GetString());
    }

    [Fact]
    public async Task An_unknown_instance_is_refused()
    {
        var (http, projectId, _) = await NewProjectAsync();
        using var owned = http;

        using var refused = await http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/agent-configs/claude-code-ghost",
            new { adapterId = "missing-adapter" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var list = await http.GetAsync($"/api/v1/projects/{projectId}/agent-configs");
        list.EnsureSuccessStatusCode();
        var configs = await ReadConfigsAsync(list);
        Assert.DoesNotContain("claude-code-ghost", configs.Keys);
    }

    private async Task<(HttpClient Http, string ProjectId, string Root)> NewProjectAsync()
    {
        var name = $"agent-api-{Guid.NewGuid():N}";
        var root = Path.Combine(fixture.ProjectRoot, name);
        Directory.CreateDirectory(root);
        var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.PostAsJsonAsync(
            "/api/v1/projects/initialize",
            new { rootPath = root, name });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (http, document.RootElement.GetProperty("id").GetString()!, root);
    }

    private static async Task<Dictionary<string, JsonElement>> ReadConfigsAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("id").GetString()!,
            item => item.Clone());
    }
}
