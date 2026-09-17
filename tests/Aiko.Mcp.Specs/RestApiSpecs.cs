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
    public async Task Template_defaults_are_edited_through_the_api_and_bump_the_version()
    {
        using var http = CreateClient();

        using var before = await http.GetAsync("api/v1/templates/default");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var original = await before.Content.ReadFromJsonAsync<JsonElement>();
        var version = original.GetProperty("version").GetInt32();
        // The built-in template states no settings at all - the installation defaults fill them, and an init
        // copies the result - but it does ship its pipelines.
        Assert.Equal(JsonValueKind.Null, original.GetProperty("settings").ValueKind);
        Assert.Equal(2, original.GetProperty("workflows").GetArrayLength());

        // Settings and pipelines are written as two slices of the same document, so neither can clobber the
        // other: the version moves once per save, and the pipelines survive a settings write.
        using var saved = await http.PutAsJsonAsync("api/v1/templates/default/settings", new
        {
            schemaVersion = 1,
            priority = new
            {
                weights = new { taskWeight = 0.6m, parentWeight = 0.4m },
                criteria = Array.Empty<object>(),
                sizes = new[]
                {
                    new { id = "M", title = "M", description = "About a day.", coefficient = 1.0m }
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var afterSettings = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(version + 1, afterSettings.GetProperty("version").GetInt32());
        Assert.Equal(2, afterSettings.GetProperty("workflows").GetArrayLength());
        Assert.Equal(1, afterSettings.GetProperty("settings").GetProperty("priority")
            .GetProperty("sizes").GetArrayLength());

        var taskWorkflow = afterSettings.GetProperty("workflows")[1];
        var workflowId = taskWorkflow.GetProperty("id").GetString();
        var revision = taskWorkflow.GetProperty("revision").GetInt64();
        using var workflowSaved = await http.PutAsJsonAsync(
            $"api/v1/templates/default/workflows/{workflowId}",
            new
            {
                title = "Tasks",
                expectedRevision = revision,
                stages = new[]
                {
                    new
                    {
                        id = "backlog",
                        title = "Backlog",
                        order = 10,
                        instruction = "Clarify the request.",
                        allowedCardKinds = new[] { "Task" },
                        defaultAgentAdapterId = (string?)null,
                        requiredArtifacts = Array.Empty<object>(),
                        actionPolicies = new Dictionary<string, string>(),
                        validationCommands = new[] { "dotnet test --no-build" },
                        skillsBeforeInstruction = new[] { "aiko-memory" },
                        skillsAfterInstruction = new[] { "aiko-report" }
                    }
                }
            });
        Assert.Equal(HttpStatusCode.OK, workflowSaved.StatusCode);
        var afterWorkflow = await workflowSaved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(version + 2, afterWorkflow.GetProperty("version").GetInt32());
        // The settings written a moment ago are still there: one version, two slices.
        Assert.Equal(0.6m, afterWorkflow.GetProperty("settings").GetProperty("priority")
            .GetProperty("weights").GetProperty("taskWeight").GetDecimal());

        // A template that does not exist is a 404 rather than a created one.
        using var missing = await http.PutAsJsonAsync("api/v1/templates/nope/settings", new { schemaVersion = 1 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_template_carries_an_initialization_instruction()
    {
        using var http = CreateClient();

        using var saved = await http.PutAsJsonAsync(
            "api/v1/templates/default",
            new { initializationInstruction = "Create src/, tests/ and docs/." });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var template = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Create src/, tests/ and docs/.",
            template.GetProperty("initializationInstruction").GetString());

        // A field left out keeps what the template already has, an empty value clears it: the two answers
        // differ, which is what makes "remove the instruction" expressible.
        using var kept = await http.PutAsJsonAsync("api/v1/templates/default", new { name = (string?)null });
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var afterKept = await kept.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Create src/, tests/ and docs/.",
            afterKept.GetProperty("initializationInstruction").GetString());

        using var cleared = await http.PutAsJsonAsync(
            "api/v1/templates/default",
            new { initializationInstruction = string.Empty });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var afterClear = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, afterClear.GetProperty("initializationInstruction").ValueKind);
    }

    [Fact]
    public async Task A_project_answers_to_its_readable_handle()
    {
        using var http = CreateClient();

        var board = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{fixture.ProjectId}/board");
        var slug = board.GetProperty("project").GetProperty("slug").GetString();
        Assert.False(string.IsNullOrWhiteSpace(slug));

        // Every project route resolves the readable handle, which is what the UI links with, while the id
        // keeps working - links saved before slugs existed still open.
        using var bySlug = await http.GetAsync($"api/v1/projects/{slug}/board");
        Assert.Equal(HttpStatusCode.OK, bySlug.StatusCode);
        var bySlugBoard = await bySlug.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            fixture.ProjectId,
            bySlugBoard.GetProperty("project").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_project_can_add_its_own_card_type()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;

        // A new type is a workflow of its own: the reserved backlog stage plus at least one working stage,
        // with its own description, icon and colour.
        using var created = await http.PostAsJsonAsync($"api/v1/projects/{project}/workflows", new
        {
            id = "epic",
            title = "Epics",
            description = "A global card type that groups several stories.",
            icon = "account-tree",
            color = "primary",
            stages = new object[]
            {
                new
                {
                    id = "backlog",
                    title = "Backlog",
                    order = 10,
                    instruction = "Clarify the epic.",
                    allowedCardKinds = new[] { "Epic" },
                    defaultAgentAdapterId = (string?)null,
                    requiredArtifacts = Array.Empty<object>(),
                    actionPolicies = new Dictionary<string, string>(),
                    icon = "inbox",
                    color = "secondary"
                },
                new
                {
                    id = "in-progress",
                    title = "In progress",
                    order = 20,
                    instruction = "Work the epic.",
                    allowedCardKinds = new[] { "Epic" },
                    defaultAgentAdapterId = (string?)null,
                    requiredArtifacts = Array.Empty<object>(),
                    actionPolicies = new Dictionary<string, string>(),
                    icon = "code",
                    color = "warning"
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The board reports the type with its own words and appearance, and its own pipeline.
        var board = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{project}/board");
        var epic = board.GetProperty("workflows").EnumerateArray()
            .Single(workflow => workflow.GetProperty("id").GetString() == "epic");
        Assert.Equal(
            "A global card type that groups several stories.",
            epic.GetProperty("description").GetString());
        Assert.Equal("account-tree", epic.GetProperty("icon").GetString());
        Assert.Equal("primary", epic.GetProperty("color").GetString());
        Assert.Equal("inbox", epic.GetProperty("stages")[0].GetProperty("icon").GetString());

        // A card of the new type is created and moved like any built-in one.
        var cardId = $"EPIC-{Guid.NewGuid():N}"[..12];
        using var card = await http.PostAsJsonAsync($"api/v1/projects/{project}/cards", new
        {
            cardId,
            kind = "Epic",
            title = "First epic",
            workflowId = "epic",
            stageId = "backlog",
            ownPriority = 3,
            declaredScopeFiles = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.Created, card.StatusCode);

        using var moved = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/cards/{cardId}/stage",
            new { stageId = "in-progress", expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // Backlog is reserved: a pipeline that drops it is refused rather than saved.
        using var withoutBacklog = await http.PutAsJsonAsync(
            $"api/v1/projects/{project}/workflows/epic",
            new
            {
                title = "Epics",
                expectedRevision = 1,
                stages = new object[]
                {
                    new
                    {
                        id = "in-progress",
                        title = "In progress",
                        order = 10,
                        instruction = "Work the epic.",
                        allowedCardKinds = new[] { "Epic" },
                        defaultAgentAdapterId = (string?)null,
                        requiredArtifacts = Array.Empty<object>(),
                        actionPolicies = new Dictionary<string, string>()
                    }
                }
            });
        Assert.Equal(HttpStatusCode.BadRequest, withoutBacklog.StatusCode);

        // A type that still has cards is not removed, so nothing is stranded.
        using var removeWithCards = await http.DeleteAsync($"api/v1/projects/{project}/workflows/epic");
        Assert.Equal(HttpStatusCode.BadRequest, removeWithCards.StatusCode);
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
    public async Task Settings_report_where_the_value_came_from()
    {
        using var http = CreateClient();
        var project = fixture.ProjectId;

        // The project was created from the base template, so it states its own settings and the view says so.
        var stated = await http.GetFromJsonAsync<JsonElement>($"api/v1/projects/{project}/settings");
        Assert.Equal("project", stated.GetProperty("executionSource").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            stated.GetProperty("snapshot").ValueKind);

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
