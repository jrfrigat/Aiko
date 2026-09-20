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
        "aiko_add_comment",
        "aiko_list_comments",
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
        "aiko_get_settings",
        "aiko_list_commands",
        "aiko_claim_command",
        "aiko_finish_command",
        "aiko_list_board",
        "aiko_list_work_queue",
        "aiko_get_card_artifact",
        "aiko_save_card_artifact"
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

    /// <summary>
    /// Registers a throwaway project and returns its id and root. The id comes from the tool's own answer,
    /// because Aiko names projects; the root is what the policy file is written under.
    /// </summary>
    private static async Task<(string Id, string Root)> InitProjectAsync(McpClient client, string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Mcp.Specs", "CrossProject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var init = await client.CallToolAsync(
            "aiko_init_project",
            new Dictionary<string, object?> { ["rootPath"] = root, ["name"] = name },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, init.IsError);
        using var document = JsonDocument.Parse(FirstText(init)!);
        return (document.RootElement.GetProperty("id").GetString()!, root);
    }

    /// <summary>
    /// Writes the cross-project policy into a project's own settings document - the same file the settings
    /// screen writes - so the daemon reads it the way it reads a policy a person set.
    /// </summary>
    private static async Task WriteCrossProjectPolicyAsync(string root, string policy, string[]? targets)
    {
        var document = new
        {
            schemaVersion = 1,
            crossProject = new { writePolicy = policy, allowedTargetProjects = targets }
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, ".aiko", "settings.json"),
            JsonSerializer.Serialize(document));
    }

    /// <summary>Creates a card in the fixture's project on behalf of <paramref name="originProjectId"/>.</summary>
    private async Task<CallToolResult> CreateCrossProjectCardAsync(
        McpClient client,
        string originProjectId,
        string cardId,
        bool userConfirmed = false)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["projectId"] = fixture.ProjectId,
            ["cardId"] = cardId,
            ["kind"] = "task",
            ["title"] = cardId,
            ["workflowId"] = "task",
            ["ownPriority"] = 1,
            ["originProjectId"] = originProjectId
        };
        if (userConfirmed)
        {
            arguments["userConfirmed"] = true;
        }

        return await client.CallToolAsync(
            "aiko_create_card_in_project",
            arguments,
            cancellationToken: CancellationToken.None);
    }

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
        // The context is what an agent reads first, so the working contract is stated here too: work happens
        // inside a started stage, and a card in its backlog has none of it yet.
        Assert.Contains("aiko_start_stage", text, StringComparison.Ordinal);
        Assert.Contains("aiko_complete_stage", text, StringComparison.Ordinal);
        Assert.Contains("One run is one stage", text, StringComparison.Ordinal);
        Assert.Contains("--all", text, StringComparison.Ordinal);
        // --all descends into the card's children as well, and this context is the first text an agent reads: a
        // person who runs an epic expecting it to create and work its stories has to find that rule here.
        Assert.Contains("descends into the card's children", text, StringComparison.Ordinal);
        // An order is still only a request: creating the card is the answer, and the run waits for the ask.
        Assert.Contains("An order is still a request", text, StringComparison.Ordinal);
        // The git rules the project states include push, and they say plainly that Aiko cannot enforce it: the
        // agent follows the rule because it read it, not because something would stop it.
        Assert.Contains("Push policy:", text, StringComparison.Ordinal);
        Assert.Contains("no push of its own", text, StringComparison.Ordinal);
        // The feed is stated as what it is: a notebook one stage leaves for the next, read before the work and
        // written before the stage is completed, with the agent's own id on the note.
        Assert.Contains("notebook one stage leaves for the next", text, StringComparison.Ordinal);
        Assert.Contains("aiko_list_comments", text, StringComparison.Ordinal);
        Assert.Contains("aiko_add_comment", text, StringComparison.Ordinal);
        Assert.Contains("adapter id", text, StringComparison.Ordinal);
        // The context names the project's git and commit policies: an agent told neither cannot know whether
        // to commit, which is how a commit lands in a repository that asked to stay local.
        Assert.Contains("## Git and commits", text, StringComparison.Ordinal);
        Assert.Contains("Git policy:", text, StringComparison.Ordinal);
        Assert.Contains("Commit policy:", text, StringComparison.Ordinal);
        Assert.Contains("aiko_report_commit", text, StringComparison.Ordinal);
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
    public async Task Board_tool_returns_the_order_the_pass_walks()
    {
        await using var client = await ConnectAsync();

        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = "task",
                ["title"] = "A card the board pass would pick up",
                ["ownPriority"] = 6
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);
        using var created = JsonDocument.Parse(FirstText(create)!);
        var cardId = created.RootElement.GetProperty("reference").GetProperty("cardId").GetString();

        // The pass over the board reads its order from here, so this tool has to answer with the same
        // snapshot the interface draws: the cards, and the priority each one is shown with.
        var board = await client.CallToolAsync(
            "aiko_list_board",
            cancellationToken: CancellationToken.None);
        Assert.False(board.IsError == true, FirstText(board));
        var boardText = FirstText(board);
        Assert.False(string.IsNullOrWhiteSpace(boardText));
        using var snapshot = JsonDocument.Parse(boardText!);

        Assert.True(snapshot.RootElement.GetProperty("cards").GetArrayLength() > 0);
        Assert.Contains(
            snapshot.RootElement.GetProperty("cards").EnumerateArray(),
            card => card.GetProperty("reference").GetProperty("cardId").GetString() == cardId);
        Assert.Contains(
            snapshot.RootElement.GetProperty("cardPriorities").EnumerateArray(),
            item => item.GetProperty("cardId").GetString() == cardId);
    }

    [Fact]
    public async Task The_command_queue_reaches_an_agent_over_mcp()
    {
        await using var client = await ConnectAsync();

        // A card of its own, so the test does not depend on what another one left on the board.
        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = "task",
                ["title"] = "A card a command can be placed for",
                ["ownPriority"] = 1
            },
            cancellationToken: CancellationToken.None);
        Assert.False(create.IsError == true, FirstText(create));
        using var created = JsonDocument.Parse(FirstText(create)!);
        var cardId = created.RootElement.GetProperty("reference").GetProperty("cardId").GetString();

        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var placed = await http.PostAsJsonAsync(
            $"api/v1/projects/{fixture.ProjectId}/commands",
            new { cardId, action = "Start", stageId = "backlog" });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);

        // The queue a screen writes to is the same one an agent reads: without this the button would place
        // a request nobody can ever see.
        var listed = await client.CallToolAsync(
            "aiko_list_commands",
            cancellationToken: CancellationToken.None);
        Assert.False(listed.IsError == true, FirstText(listed));
        var text = FirstText(listed);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(cardId!, text, StringComparison.Ordinal);
        Assert.Contains("Queued", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_project_context_requires_the_state_to_come_through_the_tools()
    {
        await using var client = await ConnectAsync();
        var context = await client.CallToolAsync(
            "aiko_get_project_context",
            cancellationToken: CancellationToken.None);
        var text = FirstText(context);
        Assert.False(string.IsNullOrWhiteSpace(text));

        // The card asked for a requirement rather than advice, so the contract itself has to say it - and to
        // name the tools, because a rule that does not say where to go instead sends an agent back to the file.
        // The sentences are wrapped in the contract, so the phrases asserted here are the ones that fit one
        // line of it.
        Assert.Contains("Do not open a file", text, StringComparison.Ordinal);
        Assert.Contains("under .aiko to find out what the project says", text, StringComparison.Ordinal);
        Assert.Contains("aiko_list_work_queue", text, StringComparison.Ordinal);
        Assert.Contains("aiko_save_card_artifact", text, StringComparison.Ordinal);
        // The exception is part of the rule: a card about the .aiko format itself is worked in those files.
        Assert.Contains("format itself is the exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_work_queue_orders_cards_and_names_what_blocks_them()
    {
        await using var client = await ConnectAsync();

        // Two cards and an edge between them: the queue has to say who waits for whom, because that is what
        // makes a pass skip a card instead of being refused on it.
        var blocking = await CreateCardAsync(client, "story", "A story the queue must rank", 9);
        var waiting = await CreateCardAsync(client, "task", "A task that waits", 1);
        var linked = await client.CallToolAsync(
            "aiko_link_cards",
            new Dictionary<string, object?>
            {
                ["sourceCardId"] = blocking,
                ["targetCardId"] = waiting,
                ["relationType"] = "blocks"
            },
            cancellationToken: CancellationToken.None);
        Assert.False(linked.IsError == true, FirstText(linked));

        var queue = await client.CallToolAsync(
            "aiko_list_work_queue",
            cancellationToken: CancellationToken.None);
        Assert.False(queue.IsError == true, FirstText(queue));
        var entries = JsonDocument.Parse(FirstText(queue)!).RootElement.EnumerateArray().ToArray();

        // The card that waits is there, with the blocker named rather than left out ...
        var waitingEntry = entries.FirstOrDefault(entry =>
            entry.GetProperty("cardId").GetString() == waiting);
        Assert.NotEqual(JsonValueKind.Undefined, waitingEntry.ValueKind);
        Assert.Contains(
            waitingEntry.GetProperty("blockedBy").EnumerateArray(),
            blocker => blocker.GetProperty("cardId").GetString() == blocking);
        Assert.False(waitingEntry.GetProperty("finished").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(waitingEntry.GetProperty("stageState").GetString()));

        // ... and the order is by the priority the board computes, not by the card's own score.
        var blockingEntry = entries.First(entry => entry.GetProperty("cardId").GetString() == blocking);
        Assert.True(
            blockingEntry.GetProperty("effectivePriority").GetDecimal() >=
            waitingEntry.GetProperty("effectivePriority").GetDecimal());
        Assert.True(entries.ToList().IndexOf(blockingEntry) < entries.ToList().IndexOf(waitingEntry));
    }

    /// <summary>Creates a card over MCP and returns its id.</summary>
    private static async Task<string> CreateCardAsync(
        McpClient client,
        string kind,
        string title,
        decimal ownPriority)
    {
        var created = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["title"] = title,
                ["ownPriority"] = ownPriority
            },
            cancellationToken: CancellationToken.None);
        Assert.False(created.IsError == true, FirstText(created));
        using var document = JsonDocument.Parse(FirstText(created)!);
        return document.RootElement.GetProperty("reference").GetProperty("cardId").GetString()!;
    }

    [Fact]
    public async Task A_card_artifact_is_read_and_written_over_mcp()
    {
        await using var client = await ConnectAsync();
        var cardId = await CreateCardAsync(client, "task", "A card whose artifacts travel", 2);

        var saved = await client.CallToolAsync(
            "aiko_save_card_artifact",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["path"] = "issue.md",
                ["content"] = "# The request\n\nEverything the card is about.\n"
            },
            cancellationToken: CancellationToken.None);
        Assert.False(saved.IsError == true, FirstText(saved));
        using var savedDocument = JsonDocument.Parse(FirstText(saved)!);
        var version = savedDocument.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));

        var read = await client.CallToolAsync(
            "aiko_get_card_artifact",
            new Dictionary<string, object?> { ["cardId"] = cardId, ["path"] = "issue.md" },
            cancellationToken: CancellationToken.None);
        Assert.False(read.IsError == true, FirstText(read));
        using var readDocument = JsonDocument.Parse(FirstText(read)!);
        Assert.Contains(
            "Everything the card is about.",
            readDocument.RootElement.GetProperty("content").GetString(),
            StringComparison.Ordinal);

        // The card carries the paths beside it and the issue document itself, so the common read is one call.
        var card = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        Assert.False(card.IsError == true, FirstText(card));
        using var cardDocument = JsonDocument.Parse(FirstText(card)!);
        Assert.Contains(
            cardDocument.RootElement.GetProperty("artifacts").EnumerateArray(),
            path => path.GetString() == "issue.md");
        Assert.Contains(
            "Everything the card is about.",
            cardDocument.RootElement.GetProperty("issue").GetString(),
            StringComparison.Ordinal);

        // A write with a stale version is refused rather than overwriting what somebody else changed.
        var stale = await client.CallToolAsync(
            "aiko_save_card_artifact",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["path"] = "issue.md",
                ["content"] = "overwritten",
                ["expectedVersion"] = "not-the-current-version"
            },
            cancellationToken: CancellationToken.None);
        Assert.True(stale.IsError == true);
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
        // next start elsewhere. A stage is completed only with a card that was re-estimated in the run.
        var estimated = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-MCP-ACTIVITY",
                ["expectedRevision"] = 2,
                ["criterionValues"] = new[] { "complete=8" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, estimated.IsError);
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
    public async Task Moves_a_card_forward_only_through_worked_stages()
    {
        await using var client = await ConnectAsync();
        var cardId = $"TASK-MOVE-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Move me",
                ["workflowId"] = "task",
                ["ownPriority"] = 2,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        // A card is created in the backlog. Declaring it reviewed from there is how a card ends up finished
        // with nothing behind it - no execution, no artifacts, no history - so the move is refused and the
        // message says what to do instead.
        var skip = await MoveAsync(client, cardId, "review", 1L);
        Assert.True(skip.IsError);
        var skipReason = FirstText(skip) ?? string.Empty;
        Assert.Contains("one stage at a time", skipReason, StringComparison.Ordinal);
        Assert.Contains("aiko_start_stage", skipReason, StringComparison.Ordinal);

        // Leaving the backlog for the next stage is how work begins.
        var intoAnalysis = await MoveAsync(client, cardId, "analysis", 1L);
        Assert.NotEqual(true, intoAnalysis.IsError);

        // And it stops there: the analysis stage has not been run, so the card cannot move on.
        var unworked = await MoveAsync(client, cardId, "implementation", 2L);
        Assert.True(unworked.IsError);
        Assert.Contains("no execution", FirstText(unworked) ?? string.Empty, StringComparison.Ordinal);

        // Pulling a card back is how rework starts, and is never refused.
        var backwards = await MoveAsync(client, cardId, "backlog", 2L);
        Assert.NotEqual(true, backwards.IsError);
    }

    /// <summary>Moves a card and returns the tool result, so a refusal can be read as an answer.</summary>
    private static ValueTask<CallToolResult> MoveAsync(
        McpClient client,
        string cardId,
        string stageId,
        long revision) =>
        client.CallToolAsync(
            "aiko_move_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["stageId"] = stageId,
                ["expectedRevision"] = revision
            },
            cancellationToken: CancellationToken.None);

    [Fact]
    public async Task Starting_the_same_stage_again_continues_it_through_mcp()
    {
        await using var client = await ConnectAsync();
        var cardId = $"TASK-CONTINUE-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Keep going",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        var first = await StartStageAsync(client, cardId, "implementation", "claude-code");
        Assert.NotEqual(true, first.IsError);
        using var firstJson = JsonDocument.Parse(FirstText(first) ?? "{}");
        var executionId = firstJson.RootElement.GetProperty("id").GetString();

        // The same stage started again is the same run: the agent picked the card back up, and the project's run
        // slot is not consumed twice.
        var again = await StartStageAsync(client, cardId, "implementation", "codex");
        Assert.NotEqual(true, again.IsError);
        using var againJson = JsonDocument.Parse(FirstText(again) ?? "{}");
        Assert.Equal(executionId, againJson.RootElement.GetProperty("id").GetString());
        Assert.Equal(2, againJson.RootElement.GetProperty("attempts").GetArrayLength());

        // A different stage while this one is open is refused, and the text says how to get on.
        var next = await StartStageAsync(client, cardId, "review", "claude-code");
        Assert.True(next.IsError);
        var reason = FirstText(next) ?? string.Empty;
        Assert.Contains("not finished", reason, StringComparison.Ordinal);
        Assert.Contains("aiko_complete_stage", reason, StringComparison.Ordinal);

        // Release the project's run slot for the rest of the class: the fixture's project is shared. The card
        // has to be re-estimated first - completing a stage with a stale readiness is refused.
        var estimated = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["expectedRevision"] = 2,
                ["criterionValues"] = new[] { "complete=8" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, estimated.IsError);
        var complete = await client.CallToolAsync(
            "aiko_complete_stage",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["actualChangedFiles"] = new[] { "src/app.cs" },
                ["artifacts"] = Array.Empty<string>()
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, complete.IsError);
    }

    [Fact]
    public async Task Completing_a_stage_without_a_fresh_estimate_is_refused_through_mcp()
    {
        await using var client = await ConnectAsync();
        var cardId = $"TASK-MCP-ESTIMATE-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Guess readiness",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        var started = await StartStageAsync(client, cardId, "implementation", "claude-code");
        Assert.NotEqual(true, started.IsError);
        using var startedJson = JsonDocument.Parse(FirstText(started) ?? "{}");
        var executionId = startedJson.RootElement.GetProperty("id").GetString();

        // The card was never estimated in this run, so its readiness describes it as it was before the work.
        var refused = await client.CallToolAsync(
            "aiko_complete_stage",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["actualChangedFiles"] = new[] { "src/app.cs" },
                ["artifacts"] = Array.Empty<string>()
            },
            cancellationToken: CancellationToken.None);
        Assert.True(refused.IsError);
        var reason = FirstText(refused) ?? string.Empty;
        Assert.Contains("aiko_estimate_card", reason, StringComparison.Ordinal);
        Assert.Contains("readiness", reason, StringComparison.Ordinal);

        // The refusal is a step, not a dead end: estimating the card lets the stage finish.
        var estimated = await client.CallToolAsync(
            "aiko_estimate_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["expectedRevision"] = 2,
                ["criterionValues"] = new[] { "complete=9" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, estimated.IsError);
        var complete = await client.CallToolAsync(
            "aiko_complete_stage",
            new Dictionary<string, object?>
            {
                ["executionId"] = executionId,
                ["actualChangedFiles"] = new[] { "src/app.cs" },
                ["artifacts"] = Array.Empty<string>()
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, complete.IsError);
    }

    [Fact]
    public async Task A_blocked_card_cannot_start_its_stage()
    {
        // A two-stage card type, so the blocker can reach the end of its own pipeline in one move: the rule reads
        // the end from the workflow, and this keeps the spec about the gate rather than about a long pipeline.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workflowId = $"waiting{suffix}";
        var kind = $"Waiting{suffix}";
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var created = await http.PostAsJsonAsync(
            $"api/v1/projects/{fixture.ProjectId}/workflows",
            new
            {
                id = workflowId,
                title = "Waiting",
                stages = new object[]
                {
                    new
                    {
                        id = "backlog", title = "Backlog", order = 10, instruction = "Clarify.",
                        allowedCardKinds = new[] { kind }, defaultAgentAdapterId = (string?)null,
                        requiredArtifacts = Array.Empty<object>(), actionPolicies = new Dictionary<string, string>()
                    },
                    new
                    {
                        id = "done", title = "Done", order = 20, instruction = "Record.",
                        allowedCardKinds = new[] { kind }, defaultAgentAdapterId = (string?)null,
                        requiredArtifacts = Array.Empty<object>(), actionPolicies = new Dictionary<string, string>()
                    }
                }
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await using var client = await ConnectAsync();
        var blockerId = $"WAITING-{suffix}";
        var blockedId = $"TASK-BLOCKED-{suffix}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = blockerId,
                ["kind"] = kind,
                ["title"] = "The card the work waits for",
                ["ownPriority"] = 1
            },
            cancellationToken: CancellationToken.None);
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = blockedId,
                ["kind"] = "task",
                ["title"] = "The card that waits",
                ["ownPriority"] = 1
            },
            cancellationToken: CancellationToken.None);
        var linked = await client.CallToolAsync(
            "aiko_link_cards",
            new Dictionary<string, object?>
            {
                ["sourceCardId"] = blockerId,
                ["targetCardId"] = blockedId,
                ["relationType"] = "blocks"
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, linked.IsError);

        // The card document says who blocks it and where that card is, so an agent reading the card sees the
        // order before it tries anything ...
        var document = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = blockedId },
            cancellationToken: CancellationToken.None);
        using var cardJson = JsonDocument.Parse(FirstText(document) ?? "{}");
        var blocker = Assert.Single(cardJson.RootElement.GetProperty("blockedBy").EnumerateArray());
        Assert.Equal(blockerId, blocker.GetProperty("cardId").GetString());
        Assert.Equal("backlog", blocker.GetProperty("stageId").GetString());

        // ... and the start is refused, naming the blocking card and its stage, so the agent has something to
        // repeat to the user instead of quietly working the blocked card.
        var refused = await StartStageAsync(client, blockedId, "analysis", "claude-code");
        Assert.True(refused.IsError);
        var refusal = FirstText(refused) ?? string.Empty;
        Assert.Contains(blockedId, refusal, StringComparison.Ordinal);
        Assert.Contains(blockerId, refusal, StringComparison.Ordinal);
        Assert.Contains("backlog", refusal, StringComparison.Ordinal);
        Assert.Contains("offer", refusal, StringComparison.Ordinal);

        // The blocker reaches the end of its own pipeline - one step out of the backlog - and the same start goes
        // through: a finished blocker holds nothing back.
        var blockerDocument = await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = blockerId },
            cancellationToken: CancellationToken.None);
        using var blockerJson = JsonDocument.Parse(FirstText(blockerDocument) ?? "{}");
        var blockerRevision = blockerJson.RootElement.GetProperty("revision").GetInt64();
        var moved = await client.CallToolAsync(
            "aiko_move_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = blockerId,
                ["stageId"] = "done",
                ["expectedRevision"] = blockerRevision
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, moved.IsError);

        // ... and the same start is no longer refused by the block: the gate reads the rule, and the domain
        // spec covers the rule itself. Whether the start goes through can still depend on runs the shared
        // project has open - the default run limit is one, and other specs may leave an execution running - so
        // what is asserted here is that the refusal is no longer about a blocker.
        var started = await StartStageAsync(client, blockedId, "analysis", "claude-code");
        Assert.DoesNotContain(blockerId, FirstText(started) ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_project_can_be_linked_to_another_one()
    {
        await using var client = await ConnectAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // A neighbour registered for this run, so the fixture's own project has something to point at.
        var neighbourRoot = Path.Combine(Path.GetTempPath(), $"aiko-neighbour-{suffix}");
        Directory.CreateDirectory(neighbourRoot);
        var neighbour = await client.CallToolAsync(
            "aiko_init_project",
            new Dictionary<string, object?>
            {
                ["rootPath"] = neighbourRoot,
                ["name"] = "Neighbour",
                ["projectId"] = $"neighbour{suffix}"
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, neighbour.IsError);
        using var neighbourJson = JsonDocument.Parse(FirstText(neighbour) ?? "{}");
        var neighbourHandle = neighbourJson.RootElement.GetProperty("handle").GetString();
        var neighbourId = neighbourJson.RootElement.GetProperty("id").GetString();

        // The link is a sentence about the neighbour, not a bare id: that sentence is what a later agent reads
        // before it decides that a piece of work belongs there.
        const string why = "the desktop client - UI work is filed here";
        var linked = await client.CallToolAsync(
            "aiko_link_project",
            new Dictionary<string, object?>
            {
                ["projectId"] = fixture.ProjectId,
                ["targetProjectId"] = neighbourHandle,
                ["description"] = why
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, linked.IsError);
        using var linkedJson = JsonDocument.Parse(FirstText(linked) ?? "{}");
        Assert.Equal(neighbourId, linkedJson.RootElement.GetProperty("projectId").GetString());

        // An agent working in this project sees the neighbour and what it is for without asking.
        var context = await client.CallToolAsync("aiko_get_project_context", cancellationToken: CancellationToken.None);
        var contextText = FirstText(context) ?? string.Empty;
        Assert.Contains("## Linked projects", contextText, StringComparison.Ordinal);
        Assert.Contains(neighbourHandle!, contextText, StringComparison.Ordinal);
        Assert.Contains(why, contextText, StringComparison.Ordinal);
        Assert.Contains("aiko_create_card_in_project", contextText, StringComparison.Ordinal);

        // A project nobody registered cannot be linked, and neither can the project itself.
        var unknown = await client.CallToolAsync(
            "aiko_link_project",
            new Dictionary<string, object?>
            {
                ["projectId"] = fixture.ProjectId,
                ["targetProjectId"] = $"missing{suffix}",
                ["description"] = why
            },
            cancellationToken: CancellationToken.None);
        Assert.True(unknown.IsError);
        var itself = await client.CallToolAsync(
            "aiko_link_project",
            new Dictionary<string, object?>
            {
                ["projectId"] = fixture.ProjectId,
                ["targetProjectId"] = fixture.ProjectId,
                ["description"] = why
            },
            cancellationToken: CancellationToken.None);
        Assert.True(itself.IsError);

        // Removing the link takes it out of the registry and out of the context.
        var unlinked = await client.CallToolAsync(
            "aiko_unlink_project",
            new Dictionary<string, object?>
            {
                ["projectId"] = fixture.ProjectId,
                ["targetProjectId"] = neighbourHandle
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, unlinked.IsError);
        using var remaining = JsonDocument.Parse(FirstText(unlinked) ?? "[]");
        Assert.Equal(0, remaining.RootElement.GetArrayLength());
    }

    /// <summary>Starts a stage and returns the tool result, so a refusal can be read as an answer.</summary>
    private static ValueTask<CallToolResult> StartStageAsync(
        McpClient client,
        string cardId,
        string stageId,
        string agentAdapterId) =>
        client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["stageId"] = stageId,
                ["agentAdapterId"] = agentAdapterId
            },
            cancellationToken: CancellationToken.None);

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

        // The origin is a project the daemon knows: its policy is what allows the hand-over, so the source must
        // be registered before it can grant anything.
        var (sourceId, sourceRoot) = await InitProjectAsync(client, "Origin source");
        await WriteCrossProjectPolicyAsync(sourceRoot, "Allow", targets: null);

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
                ["originProjectId"] = sourceId
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);
        Assert.Contains(sourceId, FirstText(create), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cross_project_writes_follow_the_source_projects_policy()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);

        var (sourceId, sourceRoot) = await InitProjectAsync(client, "Cross source");

        // Silence means no: a project that never configured the hand-over refuses it, and the refusal says what
        // to do instead rather than leaving the agent to guess.
        var denied = await CreateCrossProjectCardAsync(client, sourceId, "TASK-CROSS-DENY");
        Assert.True(denied.IsError);
        Assert.Contains("denied", FirstText(denied), StringComparison.Ordinal);

        // ask: refused until the agent has the user's word and repeats the call with the confirmation.
        await WriteCrossProjectPolicyAsync(sourceRoot, "Ask", targets: null);
        var asked = await CreateCrossProjectCardAsync(client, sourceId, "TASK-CROSS-ASK");
        Assert.True(asked.IsError);
        Assert.Contains("userConfirmed", FirstText(asked), StringComparison.Ordinal);

        var confirmed = await CreateCrossProjectCardAsync(client, sourceId, "TASK-CROSS-OK", userConfirmed: true);
        Assert.NotEqual(true, confirmed.IsError);

        // A non-empty target list narrows the policy: a target that is not in it is refused even when the
        // policy allows writing in general.
        await WriteCrossProjectPolicyAsync(sourceRoot, "Allow", ["some-other-project"]);
        var offList = await CreateCrossProjectCardAsync(client, sourceId, "TASK-CROSS-OFFLIST");
        Assert.True(offList.IsError);
        Assert.Contains("not in", FirstText(offList), StringComparison.Ordinal);

        await WriteCrossProjectPolicyAsync(sourceRoot, "Allow", targets: null);
        var allowed = await CreateCrossProjectCardAsync(client, sourceId, "TASK-CROSS-ALLOW");
        Assert.NotEqual(true, allowed.IsError);

        // Both journals saw the hand-over: the target carries the card, the source recorded that it left.
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        var historyText = await http.GetStringAsync($"api/v1/projects/{sourceId}/events/history?after=0&limit=100");
        using var history = JsonDocument.Parse(historyText);
        Assert.Contains(
            history.RootElement.EnumerateArray(),
            item => string.Equals(
                item.GetProperty("type").GetString(),
                "cross-project.card-created",
                StringComparison.Ordinal));
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

    [Fact]
    public async Task A_failing_tool_reports_the_cause_instead_of_a_generic_line()
    {
        await using var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?>
            {
                ["cardId"] = "TASK-DOES-NOT-EXIST",
                ["stageId"] = "implementation",
                ["agentAdapterId"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsError);
        var text = FirstText(result) ?? string.Empty;
        // The tool used to answer with a fixed sentence and leave the cause in the daemon's log, where an
        // agent cannot read it and therefore cannot report it either.
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);
        Assert.Contains("TASK-DOES-NOT-EXIST", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_records_the_outcome_in_the_card_discussion()
    {
        await using var client = await ConnectAsync();
        var cardId = $"TASK-COMMENT-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Record the outcome",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        var added = await client.CallToolAsync(
            "aiko_add_comment",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["body"] = "Analysis finished; no file changed yet.",
                ["author"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, added.IsError);

        // Reading it back is how an agent sees what was already said before it adds to the thread.
        var listed = await client.CallToolAsync(
            "aiko_list_comments",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        var text = FirstText(listed) ?? string.Empty;
        Assert.Contains("Analysis finished", text, StringComparison.Ordinal);
        Assert.Contains("claude-code", text, StringComparison.Ordinal);

        // And the card page's feed reads the same store, so the note an agent wrote shows up there too.
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var feed = await http.GetAsync(
            $"/api/v1/projects/{fixture.ProjectId}/cards/{cardId}/discussion",
            CancellationToken.None);
        feed.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await feed.Content.ReadAsStringAsync(CancellationToken.None));
        var entry = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("claude-code", entry.GetProperty("author").GetString());
    }

    [Fact]
    public async Task A_cards_request_and_description_carry_their_own_rules_through_mcp()
    {
        await using var client = await ConnectAsync();
        var cardId = $"TASK-TEXTS-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Fix the dropdown",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" },
                ["requirements"] = "Make the dropdown list every value.",
                ["request"] = "the dropdown is empty on Fridays"
            },
            cancellationToken: CancellationToken.None);

        using var created = JsonDocument.Parse(FirstText(await client.CallToolAsync(
            "aiko_get_card",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None)) ?? "{}");
        Assert.Equal(
            "the dropdown is empty on Fridays",
            created.RootElement.GetProperty("metadata").GetProperty("request").GetString());

        // A description that actually changed is explained in the card's own feed, signed by the agent that
        // changed it - and the tool can change that text at all, which it could not before.
        var changed = await UpdateCardAsync(
            client,
            cardId,
            expectedRevision: 1,
            requirements: "Make the dropdown list every value, ordered.",
            requirementsReason: "the list was unsorted",
            author: "claude-code");
        Assert.False(changed.IsError == true, $"aiko_update_card failed: {FirstText(changed)}");

        using var comments = JsonDocument.Parse(FirstText(await client.CallToolAsync(
            "aiko_list_comments",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None)) ?? "[]");
        var note = Assert.Single(comments.RootElement.EnumerateArray().ToArray());
        Assert.Equal("claude-code", note.GetProperty("author").GetString());
        Assert.Contains(
            "the list was unsorted",
            note.GetProperty("body").GetString()!,
            StringComparison.Ordinal);

        // The board may move a card by hand - that path is deliberately free of the pipeline rule - and once the
        // card is out of the backlog its request is a record, not a description.
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using var moved = await http.PutAsJsonAsync(
            $"api/v1/projects/{fixture.ProjectId}/cards/{cardId}/stage",
            new { stageId = "analysis", expectedRevision = 2 },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var refused = await UpdateCardAsync(
            client,
            cardId,
            expectedRevision: 3,
            request: "something else entirely");
        Assert.True(refused.IsError);
        var reason = FirstText(refused) ?? string.Empty;
        Assert.Contains("fixed", reason, StringComparison.Ordinal);
        Assert.Contains("analysis", reason, StringComparison.Ordinal);

        // The requirements are the text that keeps changing, so the same call without a request goes through.
        var later = await UpdateCardAsync(
            client,
            cardId,
            expectedRevision: 3,
            requirements: "Make the dropdown list every value, ordered, with a search box.");
        Assert.False(later.IsError == true, $"aiko_update_card failed: {FirstText(later)}");
    }

    /// <summary>
    /// Updates a card through MCP with only the texts this spec cares about; the rest of the card is sent as it
    /// already stands.
    /// </summary>
    private static ValueTask<CallToolResult> UpdateCardAsync(
        McpClient client,
        string cardId,
        long expectedRevision,
        string? requirements = null,
        string? request = null,
        string? requirementsReason = null,
        string? author = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["cardId"] = cardId,
            ["expectedRevision"] = expectedRevision,
            ["title"] = "Fix the dropdown",
            ["ownPriority"] = 1,
            ["declaredScopeFiles"] = new[] { "src/**" },
            ["actualChangedFiles"] = Array.Empty<string>()
        };
        if (requirements is not null)
        {
            arguments["requirements"] = requirements;
        }

        if (request is not null)
        {
            arguments["request"] = request;
        }

        if (requirementsReason is not null)
        {
            arguments["requirementsReason"] = requirementsReason;
        }

        if (author is not null)
        {
            arguments["author"] = author;
        }

        return client.CallToolAsync("aiko_update_card", arguments, cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task An_agent_starts_a_stage_through_the_projects_readable_handle()
    {
        // A project of its own, because the fixture's shared project allows one run at a time and another
        // spec in this class keeps a handed-off execution active on purpose: the run limit, not the route,
        // would decide the outcome here.
        var root = Path.Combine(Path.GetDirectoryName(fixture.ProjectRoot)!, "handle-project");
        Directory.CreateDirectory(root);
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        using (var initialized = await http.PostAsJsonAsync(
            "api/v1/projects/initialize",
            new { rootPath = root },
            CancellationToken.None))
        {
            initialized.EnsureSuccessStatusCode();
        }

        using var projects = JsonDocument.Parse(
            await http.GetStringAsync("api/v1/projects", CancellationToken.None));
        var project = projects.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("rootPath").GetString()!
                .EndsWith("handle-project", StringComparison.OrdinalIgnoreCase));
        var projectId = project.GetProperty("id").GetString();
        var handle = project.GetProperty("slug").GetString();
        Assert.False(string.IsNullOrWhiteSpace(handle));
        // The endpoint an agent's configuration carries is built from the handle, and the two differ.
        Assert.NotEqual(projectId, handle);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}mcp/projects/{handle}"),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        await using var client = await McpClient.CreateAsync(
            transport, cancellationToken: CancellationToken.None);

        var cardId = $"TASK-HANDLE-{Guid.NewGuid():N}";
        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Start through the handle",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);

        // This is the call that used to fail on a foreign key: the handle reached the executions table
        // instead of the immutable project id every projection is keyed by.
        var start = await client.CallToolAsync(
            "aiko_start_stage",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["stageId"] = "implementation",
                ["agentAdapterId"] = "claude-code"
            },
            cancellationToken: CancellationToken.None);
        Assert.False(
            start.IsError == true,
            $"aiko_start_stage through the handle failed: {FirstText(start)}");
        using var execution = JsonDocument.Parse(FirstText(start) ?? "{}");
        Assert.Equal(
            projectId,
            execution.RootElement.GetProperty("card").GetProperty("projectId").GetString());

        // The card's "runs" tab reads this endpoint, and it addresses the project by the handle as well.
        using var runs = await http.GetAsync(
            $"/api/v1/projects/{handle}/cards/{cardId}/executions",
            CancellationToken.None);
        runs.EnsureSuccessStatusCode();
        using var runsDocument = JsonDocument.Parse(
            await runs.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Single(runsDocument.RootElement.EnumerateArray());

        // The same route value must not reach a durable object either. A relation filed under the handle is an
        // edge the reindexer refuses, and it refuses the whole project rather than the one edge.
        var siblingId = $"TASK-HANDLE-SIBLING-{Guid.NewGuid():N}";
        var sibling = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = siblingId,
                ["kind"] = "task",
                ["title"] = "Linked through the handle",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, sibling.IsError);

        var linked = await client.CallToolAsync(
            "aiko_link_cards",
            new Dictionary<string, object?>
            {
                ["sourceCardId"] = cardId,
                ["targetCardId"] = siblingId,
                ["relationType"] = "parent-child"
            },
            cancellationToken: CancellationToken.None);
        Assert.False(
            linked.IsError == true,
            $"aiko_link_cards through the handle failed: {FirstText(linked)}");
        using var linkedDocument = JsonDocument.Parse(FirstText(linked) ?? "{}");
        Assert.Equal(
            projectId,
            linkedDocument.RootElement.GetProperty("source").GetProperty("projectId").GetString());

        // And what the file holds - what the board and the projections read - is the id too.
        using var storedRelations = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, ".aiko", "relations.json")));
        var storedRelation = Assert.Single(
            storedRelations.RootElement.GetProperty("relations").EnumerateArray().ToArray());
        Assert.Equal(
            projectId,
            storedRelation.GetProperty("source").GetProperty("projectId").GetString());
        Assert.Equal(
            projectId,
            storedRelation.GetProperty("target").GetProperty("projectId").GetString());

        // Unregister the throwaway project so the rest of this class sees the fixture's own project only;
        // its files sit under the fixture's temp root and go away with it.
        using var removed = await http.DeleteAsync(
            $"/api/v1/projects/{projectId}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }


    [Fact]
    public async Task The_event_stream_delivers_a_card_change_to_its_subscriber()
    {
        using var http = new HttpClient
        {
            BaseAddress = fixture.BaseUrl,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var response = await http.GetAsync(
            $"/api/v1/projects/{fixture.ProjectId}/events",
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var reader = new StreamReader(body);

        // A change made while the stream is open is what the board lives on: it has to arrive as a frame
        // carrying the project's immutable id, without anyone reloading the page.
        await using var client = await ConnectAsync();
        var cardId = $"TASK-SSE-{Guid.NewGuid():N}";
        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Deliver me live",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var frame = await ReadEventFrameAsync(reader, cardId, timeout.Token);
        Assert.Contains(
            $"\"projectId\":\"{fixture.ProjectId}\"",
            frame,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads server-sent event frames until one mentions <paramref name="marker"/>, and returns it.
    /// </summary>
    private static async Task<string> ReadEventFrameAsync(
        StreamReader reader,
        string marker,
        CancellationToken cancellationToken)
    {
        var frame = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new InvalidOperationException(
                    "The event stream ended before the expected change arrived.");
            }

            // A blank line closes a frame; anything else belongs to the frame being assembled.
            if (line.Length == 0)
            {
                if (frame.ToString().Contains(marker, StringComparison.Ordinal))
                {
                    return frame.ToString();
                }

                frame.Clear();
                continue;
            }

            frame.AppendLine(line);
        }
    }

    [Fact]
    public async Task Project_activity_is_served_by_the_readable_handle()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        await using var client = await ConnectAsync();
        var cardId = $"TASK-CALENDAR-{Guid.NewGuid():N}";
        await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Count me on the calendar",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);

        using var projects = JsonDocument.Parse(
            await http.GetStringAsync("api/v1/projects", CancellationToken.None));
        var handle = projects.RootElement
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("id").GetString(),
                fixture.ProjectId,
                StringComparison.Ordinal))
            .GetProperty("slug")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(handle));

        using var response = await http.GetAsync(
            $"/api/v1/projects/{handle}/activity?days=7",
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));

        // The card just created published an event, so today is on this project's own calendar - read
        // through the handle the project page holds.
        var expectedDay = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = Assert.Single(
            document.RootElement.EnumerateArray(),
            day => string.Equals(
                day.GetProperty("date").GetString(), expectedDay, StringComparison.Ordinal));
        Assert.True(today.GetProperty("count").GetInt32() >= 1);

        // An unknown project is a 404, like every other project route.
        using var unknown = await http.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid():N}/activity",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task The_ui_tool_returns_the_canonical_board_and_card_addresses()
    {
        using var http = new HttpClient { BaseAddress = fixture.BaseUrl };
        await using var client = await ConnectAsync();

        var cardId = $"TASK-OPEN-UI-{Guid.NewGuid():N}";
        var create = await client.CallToolAsync(
            "aiko_create_card",
            new Dictionary<string, object?>
            {
                ["cardId"] = cardId,
                ["kind"] = "task",
                ["title"] = "Open me in the UI",
                ["ownPriority"] = 1,
                ["declaredScopeFiles"] = new[] { "src/**" }
            },
            cancellationToken: CancellationToken.None);
        Assert.NotEqual(true, create.IsError);

        // The address has to carry the readable handle even though this client connected by the project's
        // id: the link is copied out of the answer and pasted into a chat or a ticket, so it must be the
        // address the product builds for its own pages, not a second form of it.
        using var projects = JsonDocument.Parse(
            await http.GetStringAsync("api/v1/projects", CancellationToken.None));
        var handle = projects.RootElement
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("id").GetString(),
                fixture.ProjectId,
                StringComparison.Ordinal))
            .GetProperty("slug")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(handle));
        Assert.NotEqual(fixture.ProjectId, handle);

        // No card: the board. That is where a link to a project already goes in the product - the
        // dashboard's project name and the rail's board item both build it that way.
        var board = await client.CallToolAsync(
            "aiko_open_ui",
            new Dictionary<string, object?> { ["cardId"] = null },
            cancellationToken: CancellationToken.None);
        Assert.False(board.IsError == true, FirstText(board));
        Assert.Equal($"{fixture.BaseUrl}p/{handle}/board", FirstText(board));

        // A card: its own page, the same route the board navigates to when a card is selected.
        var card = await client.CallToolAsync(
            "aiko_open_ui",
            new Dictionary<string, object?> { ["cardId"] = cardId },
            cancellationToken: CancellationToken.None);
        Assert.False(card.IsError == true, FirstText(card));
        Assert.Equal($"{fixture.BaseUrl}p/{handle}/cards/{cardId}", FirstText(card));
    }
}
