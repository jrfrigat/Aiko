using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Aiko.Mcp.Specs;

/// <summary>
/// The daemon's REST API as the PWA and external clients see it: status codes, error bodies and the
/// loopback/Host/Origin guard.
/// </summary>
/// <remarks>
/// This project is the daemon's integration suite: <see cref="McpSpecs"/> covers the MCP tool surface,
/// this class covers the transport underneath both it and the UI. It boots its own daemon through
/// <see cref="AikoServerFixture"/>, so `dotnet test Aiko.slnx` needs nothing prepared by hand.
/// </remarks>
public class RestApiSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    private HttpClient CreateClient() => new() { BaseAddress = fixture.BaseUrl };

    [Fact]
    public async Task Health_and_system_describe_the_daemon()
    {
        using var http = CreateClient();

        var health = await http.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("healthy", health.GetProperty("status").GetString());

        var system = await http.GetFromJsonAsync<JsonElement>("/api/v1/system");
        Assert.Equal("Aiko", system.GetProperty("name").GetString());
        Assert.True(system.GetProperty("processId").GetInt32() > 0);
        Assert.StartsWith(
            "http://127.0.0.1:",
            system.GetProperty("baseUrl").GetString()!,
            StringComparison.Ordinal);

        // The version is the running binary's own, not a literal: a released build announces its tag, and
        // the build metadata suffix ("0.0.0-dev+abc1234") is trimmed for the reader.
        var serverDll = AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");
        var expected = FileVersionInfo.GetVersionInfo(serverDll).ProductVersion ?? string.Empty;
        var plus = expected.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
        {
            expected = expected[..plus];
        }

        var reported = system.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(expected, reported);
        // The literal this endpoint used to answer with, whatever the build actually was.
        Assert.NotEqual("0.1.0-dev", reported);
    }

    [Fact]
    public async Task Loopback_host_and_browser_origin_are_enforced()
    {
        using var http = CreateClient();

        // A Host header that is not loopback means something is proxying the daemon. Refuse it, or the
        // pairing code and the access token are reachable from wherever that proxy listens.
        using var foreignHost = new HttpRequestMessage(HttpMethod.Get, "/health");
        foreignHost.Headers.Host = "evil.example";
        using var hostResponse = await http.SendAsync(foreignHost);
        Assert.Equal(HttpStatusCode.BadRequest, hostResponse.StatusCode);

        // A page on another origin must not be able to read the API from the user's browser.
        using var remoteOrigin = new HttpRequestMessage(HttpMethod.Get, "/health");
        remoteOrigin.Headers.Add("Origin", "https://evil.example");
        using var originResponse = await http.SendAsync(remoteOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, originResponse.StatusCode);

        // The PWA itself is served from loopback, and stays allowed.
        using var localOrigin = new HttpRequestMessage(HttpMethod.Get, "/health");
        localOrigin.Headers.Add("Origin", "http://127.0.0.1:5000");
        using var localResponse = await http.SendAsync(localOrigin);
        Assert.Equal(HttpStatusCode.OK, localResponse.StatusCode);
    }

    [Fact]
    public async Task Board_describes_the_initialized_project()
    {
        using var http = CreateClient();

        var board = await http.GetFromJsonAsync<JsonElement>(
            $"api/v1/projects/{fixture.ProjectId}/board");

        Assert.Equal(fixture.ProjectId, board.GetProperty("project").GetProperty("id").GetString());
        var workflowIds = board.GetProperty("workflows")
            .EnumerateArray()
            .Select(workflow => workflow.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains("task", workflowIds);
        Assert.Contains("story", workflowIds);
        Assert.Equal(JsonValueKind.Array, board.GetProperty("cards").ValueKind);
        Assert.Equal(JsonValueKind.Array, board.GetProperty("relations").ValueKind);

        // An unknown project is a 404, not an empty board.
        using var missing = await http.GetAsync("api/v1/projects/nope/board");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Card_lifecycle_reports_conflicts_and_validation_failures()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;
        const string cardId = "REST-CARD-1";

        using var created = await http.PostAsJsonAsync($"api/v1/projects/{project}/cards", new
        {
            cardId,
            kind = "Task",
            title = "Rest card",
            workflowId = "task",
            stageId = "backlog",
            ownPriority = 2,
            declaredScopeFiles = new[] { "src/**" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The same id twice is a conflict, not a silent overwrite.
        using var duplicate = await http.PostAsJsonAsync($"api/v1/projects/{project}/cards", new
        {
            cardId,
            kind = "Task",
            title = "Rest card",
            workflowId = "task",
            stageId = "backlog",
            ownPriority = 2
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var updated = await http.PutAsJsonAsync($"api/v1/projects/{project}/cards/{cardId}", new
        {
            title = "Rest card updated",
            ownPriority = 3,
            declaredScopeFiles = new[] { "src/**" },
            expectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(
            2,
            (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt64());

        // A stale revision is a 409 that names both revisions, so a client can reload and retry.
        using var stale = await http.PutAsJsonAsync($"api/v1/projects/{project}/cards/{cardId}", new
        {
            title = "Rest card updated again",
            ownPriority = 3,
            declaredScopeFiles = Array.Empty<string>(),
            expectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, staleBody.GetProperty("expectedRevision").GetInt64());
        Assert.Equal(2, staleBody.GetProperty("actualRevision").GetInt64());

        // A stage the card's workflow does not define is a 400.
        using var badStage = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/stage",
            new { stageId = "nope", expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, badStage.StatusCode);

        using var moved = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/stage",
            new { stageId = "implementation", expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(
            3,
            (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt64());

        // Moving a card that does not exist is a 404.
        using var unknown = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/REST-MISSING/stage",
            new { stageId = "implementation", expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var executions = await http.GetFromJsonAsync<JsonElement>(
            $"api/v1/projects/{project}/cards/{cardId}/executions");
        Assert.Equal(JsonValueKind.Array, executions.ValueKind);
    }

    [Fact]
    public async Task A_card_keeps_its_size_step_and_can_be_cleared()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;
        const string cardId = "REST-SIZE-1";

        using var created = await http.PostAsJsonAsync($"api/v1/projects/{project}/cards", new
        {
            cardId,
            kind = "Task",
            title = "Sized card",
            workflowId = "task",
            stageId = "backlog",
            ownPriority = 2,
            declaredScopeFiles = Array.Empty<string>(),
            size = "L"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            "L",
            (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("size").GetString());

        // A PUT replaces the editable fields, size included: omitting it clears the step, exactly as an
        // empty declared scope list replaces the scope. (The MCP tool is the one that treats an absent
        // size as "leave it alone", because an agent calls it with a subset of the fields.)
        using var updated = await http.PutAsJsonAsync($"api/v1/projects/{project}/cards/{cardId}", new
        {
            title = "Sized card updated",
            ownPriority = 3,
            declaredScopeFiles = Array.Empty<string>(),
            expectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("size").ValueKind);

        // Setting another step is the same field, and a blank value means no size rather than a step named
        // by whitespace.
        using var resized = await http.PutAsJsonAsync($"api/v1/projects/{project}/cards/{cardId}", new
        {
            title = "Sized card resized",
            ownPriority = 3,
            declaredScopeFiles = Array.Empty<string>(),
            expectedRevision = 2,
            size = "S"
        });
        Assert.Equal(HttpStatusCode.OK, resized.StatusCode);
        Assert.Equal(
            "S",
            (await resized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("size").GetString());

        using var cleared = await http.PutAsJsonAsync($"api/v1/projects/{project}/cards/{cardId}", new
        {
            title = "Sized card cleared",
            ownPriority = 3,
            declaredScopeFiles = Array.Empty<string>(),
            expectedRevision = 3,
            size = " "
        });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("size").ValueKind);
    }

    [Fact]
    public async Task Artifacts_round_trip_and_reject_a_stale_version()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;
        const string cardId = "REST-ARTIFACT-1";

        using var created = await http.PostAsJsonAsync($"api/v1/projects/{project}/cards", new
        {
            cardId,
            kind = "Task",
            title = "Artifacts",
            workflowId = "task",
            stageId = "backlog",
            ownPriority = 1
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // A null version creates the file; the answer carries the version to write against next time.
        using var saved = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/artifact",
            new { path = "note.md", content = "# Note", expectedVersion = (string?)null });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));

        var listed = await http.GetFromJsonAsync<JsonElement>(
            $"api/v1/projects/{project}/cards/{cardId}/artifacts");
        Assert.Contains(listed.EnumerateArray(), item => item.GetProperty("path").GetString() == "note.md");

        var read = await http.GetFromJsonAsync<JsonElement>(
            $"api/v1/projects/{project}/cards/{cardId}/artifact?path=note.md");
        Assert.Equal("# Note", read.GetProperty("content").GetString());

        using var overwritten = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/artifact",
            new { path = "note.md", content = "# Note 2", expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, overwritten.StatusCode);

        // Writing again from the same starting version is a conflict: the file moved on.
        using var stale = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/artifact",
            new { path = "note.md", content = "# Note 3", expectedVersion = version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Reading a path that does not exist is a 404.
        using var missing = await http.GetAsync(
            $"api/v1/projects/{project}/cards/{cardId}/artifact?path=missing.md");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Workflow_update_validates_input_and_revisions()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;

        var board = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{project}/board");
        var workflow = board.GetProperty("workflows")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "task");
        var revision = workflow.GetProperty("revision").GetInt64();
        var stages = workflow.GetProperty("stages").EnumerateArray().ToArray();

        // A workflow without stages is refused before anything is written.
        using var empty = await http.PutAsJsonAsync($"api/v1/projects/{project}/workflows/task", new
        {
            title = "Task",
            stages = Array.Empty<object>(),
            expectedRevision = revision
        });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // An outdated revision is a 409.
        using var stale = await http.PutAsJsonAsync($"api/v1/projects/{project}/workflows/task", new
        {
            title = "Task",
            stages,
            expectedRevision = revision - 1
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Changing only the title keeps every stage and bumps the revision.
        using var renamed = await http.PutAsJsonAsync($"api/v1/projects/{project}/workflows/task", new
        {
            title = "Task pipeline",
            stages,
            expectedRevision = revision
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var renamedBody = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Task pipeline", renamedBody.GetProperty("title").GetString());
        Assert.Equal(revision + 1, renamedBody.GetProperty("revision").GetInt64());

        // An unknown workflow is a 404.
        using var unknown = await http.PutAsJsonAsync($"api/v1/projects/{project}/workflows/nope", new
        {
            title = "Nope",
            stages,
            expectedRevision = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Settings_report_which_level_supplied_the_value()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;

        using var global = await http.PutAsJsonAsync("/api/v1/settings", new
        {
            schemaVersion = 1,
            execution = new
            {
                workspaceMode = "Shared",
                maxConcurrentRuns = 3,
                scopeOverlapPolicy = "Ask",
                sharedCheckoutCommitPolicy = "Deny"
            }
        });
        Assert.Equal(HttpStatusCode.OK, global.StatusCode);

        // The project has no settings of its own, so it reads the global level.
        var inherited = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{project}/settings");
        Assert.Equal("global", inherited.GetProperty("executionSource").GetString());
        Assert.Equal(
            3,
            inherited.GetProperty("effectiveExecution").GetProperty("maxConcurrentRuns").GetInt32());

        using var projectLevel = await http.PutAsJsonAsync($"api/v1/projects/{project}/settings", new
        {
            schemaVersion = 1,
            execution = new
            {
                workspaceMode = "Worktree",
                maxConcurrentRuns = 1,
                scopeOverlapPolicy = "Deny",
                sharedCheckoutCommitPolicy = "Deny"
            }
        });
        Assert.Equal(HttpStatusCode.OK, projectLevel.StatusCode);

        // Now the project level wins, and the view says so.
        var overridden = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{project}/settings");
        Assert.Equal("project", overridden.GetProperty("executionSource").GetString());
        Assert.Equal(
            1,
            overridden.GetProperty("effectiveExecution").GetProperty("maxConcurrentRuns").GetInt32());
        Assert.Equal(
            "Worktree",
            overridden.GetProperty("effectiveExecution").GetProperty("workspaceMode").GetString());
    }

    [Fact]
    public async Task Agents_are_discovered()
    {
        using var http = CreateClient();

        var agents = await http.GetFromJsonAsync<JsonElement>("api/v1/agents");

        Assert.Equal(JsonValueKind.Array, agents.ValueKind);
        var ids = agents.EnumerateArray()
            .Select(agent => agent.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains("claude-code", ids);
        Assert.Contains("codex", ids);
    }
    [Fact]
    public async Task Agent_connection_endpoints_report_the_state_they_produce()
    {
        using var http = CreateClient();

        // Connect: the answer carries the adapter as it is now plus the per-file outcome, so a UI needs
        // one round trip and cannot show a state older than the click.
        using var connected = await http.PostAsync("/api/v1/agents/cursor/installation", content: null);
        connected.EnsureSuccessStatusCode();
        var connectedBody = await connected.Content.ReadFromJsonAsync<JsonElement>();
        var connectedAdapter = connectedBody.GetProperty("adapter");
        Assert.Equal("cursor", connectedAdapter.GetProperty("id").GetString());
        var connectedScope = connectedAdapter.GetProperty("userScope");
        Assert.Equal(
            connectedScope.GetProperty("expectedFiles").GetInt32(),
            connectedScope.GetProperty("configuredFiles").GetInt32());

        // Disconnect: nothing of Aiko's is left in the user scope.
        using var disconnected = await http.DeleteAsync("/api/v1/agents/cursor/installation");
        disconnected.EnsureSuccessStatusCode();
        var disconnectedBody = await disconnected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            0,
            disconnectedBody
                .GetProperty("adapter")
                .GetProperty("userScope")
                .GetProperty("configuredFiles")
                .GetInt32());

        // An adapter the daemon does not know is a 404 in both directions.
        using var unknownConnect = await http.PostAsync("/api/v1/agents/nope/installation", content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknownConnect.StatusCode);
        using var unknownDisconnect = await http.DeleteAsync("/api/v1/agents/nope/installation");
        Assert.Equal(HttpStatusCode.NotFound, unknownDisconnect.StatusCode);
    }


}
