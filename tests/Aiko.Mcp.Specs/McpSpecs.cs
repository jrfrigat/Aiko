using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Aiko.Mcp.Specs;

public class McpSpecs(AikoServerFixture fixture) : IClassFixture<AikoServerFixture>
{
    private static readonly string[] RequiredTools =
    [
        "aiko_get_project_context",
        "aiko_list_cards",
        "aiko_get_card",
        "aiko_create_card",
        "aiko_estimate_card",
        "aiko_update_card",
        "aiko_move_card",
        "aiko_link_cards",
        "aiko_take_card",
        "aiko_start_stage",
        "aiko_report_progress",
        "aiko_request_scope_expansion",
        "aiko_complete_stage",
        "aiko_pause_execution",
        "aiko_handoff_execution",
        "aiko_resume_execution",
        "aiko_report_agent_state",
        "aiko_report_commit",
        "aiko_approve_commit",
        "aiko_search_memory",
        "aiko_store_memory",
        "aiko_open_ui",
        "aiko_init_project",
        "aiko_list_projects",
        "aiko_list_templates",
        "aiko_create_card_in_project",
        "aiko_doctor",
        "aiko_reindex",
        "aiko_backup",
        "aiko_token",
        "aiko_get_settings"
    ];

    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = fixture.McpEndpoint,
            TransportMode = HttpTransportMode.StreamableHttp
        });

        return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
    }

    private static string? FirstText(CallToolResult result) =>
        result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault();

    [Fact]
    public async Task Discovers_all_required_tools_over_streamable_http()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        foreach (var requiredTool in RequiredTools)
        {
            Assert.Contains(tools, tool => string.Equals(tool.Name, requiredTool, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Project_context_tool_returns_usable_context()
    {
        await using var client = await ConnectAsync();

        var context = await client.CallToolAsync(
            "aiko_get_project_context",
            cancellationToken: CancellationToken.None);
        var text = FirstText(context);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Aiko project context", text, StringComparison.Ordinal);
        // The card types come from the project's own workflows: an agent that does not learn them here
        // cannot create a card of a type the user added.
        Assert.Contains("## Card types of this project", text, StringComparison.Ordinal);
        Assert.Contains("### Story (workflowId: story)", text, StringComparison.Ordinal);
        Assert.Contains("### Task (workflowId: task)", text, StringComparison.Ordinal);
        Assert.Contains("backlog: a new card starts here", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_context_carries_the_initialization_instruction_of_the_project()
    {
        // The fixture project was created from a template that asks for nothing, so the document an init
        // copies is written here: what is under test is that the context hands it to the agent, and that it
        // stops doing so once there is nothing to hand over.
        var document = Path.Combine(fixture.ProjectRoot, ".aiko", "initialization.md");
        await File.WriteAllTextAsync(document, "Create src/, tests/ and docs/, and add an .editorconfig.\n");
        try
        {
            await using var client = await ConnectAsync();
            var context = await client.CallToolAsync(
                "aiko_get_project_context",
                cancellationToken: CancellationToken.None);
            var text = FirstText(context);
            Assert.Contains("## Project initialization instruction", text, StringComparison.Ordinal);
            Assert.Contains("Create src/, tests/ and docs/", text, StringComparison.Ordinal);

            File.Delete(document);
            var after = await client.CallToolAsync(
                "aiko_get_project_context",
                cancellationToken: CancellationToken.None);
            Assert.DoesNotContain(
                "## Project initialization instruction",
                FirstText(after),
                StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(document))
            {
                File.Delete(document);
            }
        }
    }

    [Fact]
    public async Task Project_context_describes_a_card_type_the_project_added()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var created = await http.PostAsJsonAsync(
            $"api/v1/projects/{fixture.ProjectId}/workflows",
            new
            {
                id = "bug",
                title = "Bugs",
                description = "Something that does not work.",
                stages = new object[]
                {
                    new
                    {
                        id = "backlog",
                        title = "Backlog",
                        order = 10,
                        instruction = "Clarify the bug.",
                        allowedCardKinds = new[] { "Bug" },
                        defaultAgentAdapterId = (string?)null,
                        requiredArtifacts = Array.Empty<object>(),
                        actionPolicies = new Dictionary<string, string>()
                    }
                }
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using var client = await ConnectAsync();
        var context = await client.CallToolAsync(
            "aiko_get_project_context",
            cancellationToken: CancellationToken.None);
        var text = FirstText(context);
        Assert.Contains("### Bug (workflowId: bug)", text, StringComparison.Ordinal);
        Assert.Contains("Something that does not work.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creates_and_reads_a_card_through_mcp()
    {
        await using var client = await ConnectAsync();

        // No id and no stage: Aiko names the card and lands it in backlog.
        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = "task",
                ["title"] = "Verify MCP mutation",
                ["ownPriority"] = 5,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);
        var createdText = FirstText(create);
        Assert.False(string.IsNullOrWhiteSpace(createdText));
        using var created = JsonDocument.Parse(createdText!);
        var cardId = created.RootElement.GetProperty("reference").GetProperty("cardId").GetString();
        Assert.StartsWith("TASK-", cardId, StringComparison.Ordinal);
        Assert.Equal("backlog", created.RootElement.GetProperty("stageId").GetString());

        var get = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        var cardText = FirstText(get);
        Assert.False(string.IsNullOrWhiteSpace(cardText));
        Assert.Contains(cardId!, cardText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_browser_endpoint_lists_directories_over_http()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Mcp.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "child"));
        try
        {
            using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
            using var response = await http.GetAsync(
                $"/api/v1/fs/directories?path={Uri.EscapeDataString(root)}",
                CancellationToken.None);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal("child", Assert.Single(entries).GetProperty("name").GetString());

            // A path that is not a usable directory is a 400 with an explanation, not a stack trace.
            using var bad = await http.GetAsync(
                $"/api/v1/fs/directories?path={Uri.EscapeDataString(Path.Combine(root, "missing"))}",
                CancellationToken.None);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Removing_a_project_unregisters_it_and_keeps_its_files()
    {
        // A throwaway project: the fixture's own project is shared by every spec in this class.
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Mcp.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
            using var content = new StringContent(
                JsonSerializer.Serialize(new { rootPath = root }),
                Encoding.UTF8,
                "application/json");
            using var created = await http.PostAsync("/api/v1/projects/initialize", content);
            created.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
            var projectId = document.RootElement.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(projectId));

            using var removed = await http.DeleteAsync($"/api/v1/projects/{projectId}");
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

            var projects = await http.GetStringAsync("/api/v1/projects");
            Assert.DoesNotContain(projectId!, projects, StringComparison.Ordinal);
            // The registration goes; the project's .aiko directory stays.
            Assert.True(Directory.Exists(Path.Combine(root, ".aiko")));

            // Removing a project that is already gone is a 404, not a silent success.
            using var again = await http.DeleteAsync($"/api/v1/projects/{projectId}");
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Doctor_tool_reports_the_installation()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync(
            "aiko_doctor",
            cancellationToken: CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var text = FirstText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));

        // The report is the same one `aiko doctor` prints: the database, the token and the projects, each
        // with its own verdict line.
        Assert.Contains("Database present.", text, StringComparison.Ordinal);
        Assert.Contains("Access token present.", text, StringComparison.Ordinal);
        Assert.Contains("Daemon port", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_endpoint_reports_the_day_of_a_started_stage()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-ACTIVITY",
                ["kind"] = "task",
                ["title"] = "Verify activity",
                ["workflowId"] = "task",
                ["stageId"] = "implementation",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        var start = await client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-ACTIVITY",
                ["stageId"] = "implementation",
                ["agentAdapterId"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, start.IsError);
        using var startDocument = JsonDocument.Parse(FirstText(start) ?? "{}");
        var executionId = startDocument.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(executionId));

        // The dashboard's contribution graph reads this endpoint over HTTP, so the round trip - and in
        // particular the date serialization of the payload - is what is pinned here.
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var response = await http.GetAsync("/api/v1/activity?days=7", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(), cancellationToken: CancellationToken.None);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var days = document.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(days);

        var expectedDay = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = Assert.Single(
            days,
            day => string.Equals(day.GetProperty("date").GetString(), expectedDay, StringComparison.Ordinal));
        Assert.True(today.GetProperty("count").GetInt32() >= 1);

        // Release the project's run slot. A started execution counts against maxConcurrentRuns, and the
        // fixture's project is shared by every spec in this class - leaving it active would fail the
        // next start elsewhere.
        var complete = await client.CallToolAsync(
            "aiko_complete_stage",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["actualChangedFiles"] = new[] { "src/app.cs" },
                ["artifacts"] = new[] { "implementation.md" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, complete.IsError);
    }

    [Fact]
    public async Task Rate_limited_handoff_preserves_attempt_history()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-HANDOFF",
                ["kind"] = "task",
                ["title"] = "Verify handoff",
                ["workflowId"] = "task",
                ["stageId"] = "implementation",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        await client.CallToolAsync(
            "aiko_take_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-HANDOFF",
                ["agentAdapterId"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);

        var start = await client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-HANDOFF",
                ["stageId"] = "implementation",
                ["agentAdapterId"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);
        using var startJson = JsonDocument.Parse(FirstText(start)!);
        var executionId = startJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Started execution has no id.");

        await client.CallToolAsync(
            "aiko_report_agent_state",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["state"] = "rate-limited",
                ["exitReason"] = "Claude usage limit reached."
            },
            cancellationToken: CancellationToken.None);
        var handoff = await client.CallToolAsync(
            "aiko_handoff_execution",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["targetAgentAdapterId"] = "codex"
            },
            cancellationToken: CancellationToken.None);
        using var handoffJson = JsonDocument.Parse(FirstText(handoff)!);
        var attempts = handoffJson.RootElement.GetProperty("attempts");
        Assert.Equal(2, attempts.GetArrayLength());
        Assert.Equal("RateLimited", attempts[0].GetProperty("state").GetString());
        Assert.Equal("codex", attempts[1].GetProperty("agentAdapterId").GetString());
    }

    [Fact]
    public async Task Sse_stream_replays_history_and_delivers_live_events()
    {
        await using var client = await ConnectAsync();
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-SSE-REPLAY",
                ["kind"] = "task",
                ["title"] = "SSE replay",
                ["workflowId"] = "task",
                ["stageId"] = "backlog",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{fixture.BaseUrl}api/v1/projects/{fixture.ProjectId}/events");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("Last-Event-Id", "0");
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);
        response.EnsureSuccessStatusCode();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        var frames = new List<string>();
        var sawReplay = false;
        var sawLive = false;
        while (!sawReplay || !sawLive)
        {
            var lineTask = reader.ReadLineAsync(cancellation.Token).AsTask();
            var completed = await Task.WhenAny(
                lineTask,
                Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token));
            if (completed != lineTask)
            {
                throw new TimeoutException("The SSE stream did not deliver the expected events.");
            }

            var line = await lineTask;
            if (line is null)
            {
                throw new InvalidOperationException("The SSE stream closed unexpectedly.");
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var frame = line["data: ".Length..];
            frames.Add(frame);
            if (frame.Contains("TASK-SSE-REPLAY", StringComparison.Ordinal))
            {
                sawReplay = true;
            }

            if (sawReplay && !sawLive)
            {
                await client.CallToolAsync(
                    "aiko_create_card",
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = "TASK-SSE-LIVE",
                        ["kind"] = "task",
                        ["title"] = "SSE live",
                        ["workflowId"] = "task",
                        ["stageId"] = "backlog",
                        ["ownPriority"] = 1,
                        ["declaredScopeFiles"] = new[] { "src/**" }
                    },
                    cancellationToken: CancellationToken.None);
            }

            if (frame.Contains("TASK-SSE-LIVE", StringComparison.Ordinal))
            {
                sawLive = true;
            }
        }

        Assert.NotEmpty(frames);
    }

    [Fact]
    public async Task Stdio_proxy_forwards_the_remote_tool_list_and_calls()
    {
        await using var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        var proxyAssembly = fixture.FindStdioProxyAssembly();
        var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Aiko stdio proxy",
            Command = "dotnet",
            Arguments = [proxyAssembly, "--url", fixture.McpEndpoint.ToString()],
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        });
        await using var stdioClient = await McpClient.CreateAsync(
            stdioTransport,
            cancellationToken: CancellationToken.None);
        var stdioTools = await stdioClient.ListToolsAsync(cancellationToken: CancellationToken.None);
        Assert.Equal(tools.Count, stdioTools.Count);
        Assert.Contains(stdioTools, tool => tool.Name == "aiko_get_project_context");

        var stdioContext = await stdioClient.CallToolAsync(
            "aiko_get_project_context",
            cancellationToken: CancellationToken.None);
        var stdioText = FirstText(stdioContext);
        Assert.False(string.IsNullOrWhiteSpace(stdioText));
        Assert.Contains("Aiko project context", stdioText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moves_a_card_between_stages_through_mcp()
    {
        await using var client = await ConnectAsync();

        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MOVE-001",
                ["kind"] = "task",
                ["title"] = "Move me",
                ["workflowId"] = "task",
                ["stageId"] = "backlog",
                ["ownPriority"] = 2,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        var move = await client.CallToolAsync(
            "aiko_move_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MOVE-001",
                ["stageId"] = "implementation",
                ["expectedRevision"] = 1L
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, move.IsError);
        Assert.Contains("implementation", FirstText(move), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daemon_diagnostics_and_reindex_tools_work()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var doctor = await client.CallToolAsync("aiko_doctor", cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, doctor.IsError);
        var report = FirstText(doctor);
        Assert.Contains("Database present.", report, StringComparison.Ordinal);
        // The daemon-level report covers every registered project, each with its own verdict.
        Assert.Contains("registered and present.", report, StringComparison.Ordinal);

        // One project can be inspected on its own.
        var scoped = await client.CallToolAsync(
            "aiko_doctor",
            new Dictionary<string, object?> { ["projectId"] = fixture.ProjectId },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, scoped.IsError);
        Assert.Contains("registered and present.", FirstText(scoped), StringComparison.Ordinal);

        var reindex = await client.CallToolAsync(
            "aiko_reindex",
            new Dictionary<string, object?> { ["projectId"] = fixture.ProjectId },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, reindex.IsError);
    }

    [Fact]
    public async Task Daemon_create_card_records_cross_project_origin()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var create = await client.CallToolAsync(
            "aiko_create_card_in_project",
            new Dictionary<string, object?>
            {
                ["projectId"] = fixture.ProjectId,
                ["cardId"] = "TASK-XPROJ-001",
                ["kind"] = "task",
                ["title"] = "Cross project",
                ["workflowId"] = "task",
                ["stageId"] = "backlog",
                ["ownPriority"] = 3,
                ["declaredScopeFiles"] = new[] { "lib/**" },
                ["originProjectId"] = "OTHER-PROJECT"
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);
        Assert.Contains("OTHER-PROJECT", FirstText(create), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daemon_endpoint_lists_and_initializes_projects()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var newRoot = Path.Combine(Path.GetTempPath(), "Aiko.Mcp.Specs", "DaemonInit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(newRoot);
        var init = await client.CallToolAsync(
            "aiko_init_project",
            new Dictionary<string, object?> { ["rootPath"] = newRoot, ["name"] = "Daemon init" },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, init.IsError);

        var list = await client.CallToolAsync("aiko_list_projects", cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, list.IsError);
        Assert.Contains("Daemon init", FirstText(list), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secured_server_requires_authentication_and_supports_pairing()
    {
        var serverDll = AikoServerFixture.FindRepositoryBinary("Aiko.Server", "Aiko.Server.dll");
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Secured", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "project"));

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverDll}\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["AIKO_DATABASE"] = Path.Combine(root, "data", "aiko.db");
        startInfo.EnvironmentVariables["AIKO_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        startInfo.EnvironmentVariables["AIKO_TOKEN"] = "test-token-123";
        startInfo.EnvironmentVariables["AIKO_PAIR_CODE"] = "pair-123";

        using var server = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Aiko server.");
        try
        {
            using var httpClient = new HttpClient { BaseAddress = baseUrl };
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var health = await httpClient.GetAsync("/health");
                    if (health.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // The server has not started listening yet.
                }

                await Task.Delay(250);
            }

            using (var unauthenticated = await httpClient.GetAsync("/api/v1/projects"))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            }

            using (var authorized = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects"))
            {
                authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-123");
                using var response = await httpClient.SendAsync(authorized);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using (var pairRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pair-request"))
            {
                pairRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-123");
                using var pairResponse = await httpClient.SendAsync(pairRequest);
                Assert.Equal(HttpStatusCode.OK, pairResponse.StatusCode);
            }

            using (var unauthenticatedPairRequest = await httpClient.PostAsync(
                       "/api/v1/auth/pair-request",
                       new StringContent("{}", Encoding.UTF8, "application/json")))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedPairRequest.StatusCode);
            }

            using (var badPair = new StringContent("{\"code\":\"wrong\"}", Encoding.UTF8, "application/json"))
            using (var badPairResponse = await httpClient.PostAsync("/api/v1/auth/pair", badPair))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, badPairResponse.StatusCode);
            }

            using (var goodPair = new StringContent("{\"code\":\"pair-123\"}", Encoding.UTF8, "application/json"))
            using (var goodPairResponse = await httpClient.PostAsync("/api/v1/auth/pair", goodPair))
            {
                Assert.Equal(HttpStatusCode.OK, goodPairResponse.StatusCode);
            }

            using (var cookieResponse = await httpClient.GetAsync("/api/v1/projects"))
            {
                Assert.Equal(HttpStatusCode.OK, cookieResponse.StatusCode);
            }

            // The daemon can be asked to stop, which is what `aiko serve stop` does: refused without the token
            // like every other API call, and fatal with it. The command therefore has a contract to keep
            // rather than a process to kill.
            // Refused without the token, like every other API call. A cookie-free client on purpose: the
            // pairing cookie this spec set a moment ago would otherwise authenticate the request, and the
            // check would pass for the wrong reason - by stopping the daemon.
            using (var anonymous = new HttpClient(new HttpClientHandler { UseCookies = false })
                   {
                       BaseAddress = baseUrl
                   })
            {
                using var unauthenticatedStop = await anonymous.PostAsync("/api/v1/system/shutdown", null);
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedStop.StatusCode);
            }

            using (var stillUp = await httpClient.GetAsync("/health"))
            {
                Assert.Equal(HttpStatusCode.OK, stillUp.StatusCode);
            }

            // A client with a short timeout of its own: once the daemon goes away a request either fails at
            // once or hangs, and the poll must not wait out the default hundred seconds to learn which.
            using (var stopping = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(2) })
            {
                using var stop = new HttpRequestMessage(HttpMethod.Post, "/api/v1/system/shutdown");
                stop.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token-123");
                using var stopResponse = await stopping.SendAsync(stop);
                Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
                var answer = await stopResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(answer.GetProperty("processId").GetInt32() > 0);

                var stopped = false;
                for (var attempt = 0; attempt < 40 && !stopped; attempt++)
                {
                    await Task.Delay(250);
                    try
                    {
                        using var health = await stopping.GetAsync("/health");
                    }
                    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                    {
                        stopped = true;
                    }
                }

                Assert.True(stopped, "The daemon kept answering after it accepted a shutdown.");
            }
        }
        finally
        {
            // The last part of this spec stops the daemon through its own endpoint, so it may already be gone
            // by the time cleanup runs.
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
            }

            await server.WaitForExitAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Api_rejects_remote_host_and_origin()
    {
        using var httpClient = new HttpClient { BaseAddress = fixture.BaseUrl };

        using (var badHost = new HttpRequestMessage(HttpMethod.Get, "/health"))
        {
            badHost.Headers.Host = "evil.example.com";
            using var response = await httpClient.SendAsync(badHost);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var badOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/projects"))
        {
            badOrigin.Headers.Add("Origin", "https://evil.example.com");
            using var response = await httpClient.SendAsync(badOrigin);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Api_maps_client_errors_to_stable_status_codes()
    {
        using var httpClient = new HttpClient { BaseAddress = fixture.BaseUrl };

        using (var notFound = await httpClient.GetAsync($"/api/v1/projects/{Guid.NewGuid():N}/board"))
        {
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        }

        using (var sseNotFound = await httpClient.GetAsync($"/api/v1/projects/{Guid.NewGuid():N}/events"))
        {
            Assert.Equal(HttpStatusCode.NotFound, sseNotFound.StatusCode);
        }

        using (var badBody = new StringContent("{ not-json ", Encoding.UTF8, "application/json"))
        using (var badRequest = await httpClient.PostAsync("/api/v1/projects/initialize", badBody))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        }
    }
}
