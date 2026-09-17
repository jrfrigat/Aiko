namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Templates of the text files Aiko installs into a project (skills, commands, rules).
/// </summary>
internal static class AgentTemplates
{
    /// <summary>
    /// Skill with instructions for coordinating work through the Aiko MCP.
    /// </summary>
    public const string Skill =
        """
        ---
        name: aiko
        description: Use Aiko MCP for project cards, workflow stages, execution history and handoff.
        ---

        When Aiko MCP is available, read the project context before planning project work.
        Treat Aiko cards as the durable source of task state: any piece of work starts with a card.
        Before changing files outside the card's scopeFiles, warn the user and record the proposed
        scope expansion through aiko_request_scope_expansion. Record progress, actual changed files,
        commits (aiko_report_commit) and handoff state through Aiko MCP. On rate limit, report the
        agent state and hand the execution to another agent without losing history.
        """;

    /// <summary>
    /// Slash command that creates a story card.
    /// </summary>
    public const string StoryCreate =
        """
        Create a story card in Aiko. Pick a file-safe id (for example STORY-001), a clear title, the
        story workflow id and the initial stage (normally backlog), then call aiko_create_card with
        kind=story and an own priority. Report the created card id.
        """;

    /// <summary>
    /// Slash command that creates a task card.
    /// </summary>
    public const string TaskCreate =
        """
        Create a task card in Aiko. Pick a file-safe id (for example TASK-001), a clear title, the
        task workflow id and the initial stage (normally backlog), then call aiko_create_card with
        kind=task, an own priority and the declared scope files. Report the created card id.
        """;

    /// <summary>
    /// Slash command that moves a card to the next workflow stage.
    /// </summary>
    public const string NextStage =
        """
        Move the current card to the next stage of its workflow. Read the card with aiko_get_card to
        get its current stage and revision, determine the next stage, then call aiko_move_card with
        that stage and the card's revision.
        """;

    /// <summary>
    /// Slash command for the analysis stage.
    /// </summary>
    public const string Analyze =
        """
        Analyze the current card. Read the project context and the card, start the analysis stage
        with aiko_start_stage, produce the required analysis artifact, report progress with
        aiko_report_progress and finish with aiko_complete_stage.
        """;

    /// <summary>
    /// Slash command for the implementation stage.
    /// </summary>
    public const string Implement =
        """
        Implement the current card. Start the implementation stage with aiko_start_stage, make the
        changes within the declared scope, record actual changed files with aiko_report_progress and
        finish with aiko_complete_stage.
        """;

    /// <summary>
    /// Slash command for the review stage.
    /// </summary>
    public const string Review =
        """
        Review the current card. Start the review stage with aiko_start_stage, check the tests and the
        deviations from the declared scope, report progress and finish with aiko_complete_stage.
        """;

    /// <summary>
    /// Slash command for completing the current card.
    /// </summary>
    public const string Complete =
        """
        Complete the current card's stage. Record the actual changed files and produced artifacts, then
        call aiko_complete_stage. Store durable conclusions with aiko_store_memory where useful.
        """;

    /// <summary>
    /// Slash command for requesting a scope expansion.
    /// </summary>
    public const string Scope =
        """
        The work needs files outside the card's declaredScopeFiles. Call aiko_request_scope_expansion
        with the additional files or glob patterns and the reason, then wait for the user's decision.
        """;

    /// <summary>
    /// Slash command for handing the execution to another agent.
    /// </summary>
    public const string Handoff =
        """
        The current agent cannot continue this execution (for example after a rate limit). Report the
        state with aiko_report_agent_state (rate-limited or failed) and hand the execution to another
        agent with aiko_handoff_execution, preserving the full attempt history.
        """;

    /// <summary>
    /// Slash command for searching and storing durable memory.
    /// </summary>
    public const string Memory =
        """
        Work with the durable project memory: search it with aiko_search_memory before deciding, and
        store decisions, conventions and lessons with aiko_store_memory.
        """;

    /// <summary>
    /// Slash command that summarizes the work in progress.
    /// </summary>
    public const string Status =
        """
        Summarize the current work: list cards with aiko_list_cards and their executions to report
        what is in progress, what is waiting for the user and what is done.
        """;

    /// <summary>
    /// Global skill installed at user scope: coordinates Aiko without a project.
    /// </summary>
    public const string GlobalSkill =
        """
        ---
        name: aiko
        description: Initialize and manage Aiko projects through the local daemon.
        ---

        Aiko coordinates project work between agents. Use the global commands to register the
        current project (/aiko-init), list projects (/aiko-list-projects), check the daemon
        (/aiko-status), diagnose it (/aiko-doctor), repair what the diagnosis found (/aiko-repair),
        manage the agents' integrations (/aiko-agents), show the access token (/aiko-token), back a
        project up (/aiko-backup) and open the UI (/aiko-ui). After /aiko-init, restart this agent so
        the project-scoped MCP configuration and skills are loaded.
        """;

    /// <summary>
    /// Global slash command that registers the current project.
    /// </summary>
    public const string Init =
        """
        Register the current directory as an Aiko project. Run `aiko init <path> [--name <n>]
        [--git-policy <p>]` in the terminal (Aiko is installed and on PATH), then restart this
        agent so the project-scoped MCP configuration and skills are loaded.
        """;

    /// <summary>
    /// Global slash command that lists the registered projects.
    /// </summary>
    public const string ListProjects =
        """
        List the projects registered with the local Aiko daemon. Use the daemon MCP tool
        aiko_list_projects, or run `aiko status` in the terminal.
        """;

    /// <summary>
    /// Global slash command that shows the daemon status.
    /// </summary>
    public const string GlobalStatus =
        """
        Show the Aiko daemon status. Run `aiko status` in the terminal to see the data directory,
        database path, port and daemon health.
        """;

    /// <summary>
    /// Global slash command that opens the UI without a project.
    /// </summary>
    public const string GlobalUi =
        """
        Open the Aiko UI in the browser. Run `aiko ui` in the terminal, or open the daemon URL.
        """;

    /// <summary>
    /// Global slash command that diagnoses the local installation without changing it.
    /// </summary>
    public const string GlobalDoctor =
        """
        Diagnose the local Aiko installation. Use the daemon MCP tool aiko_doctor (it changes
        nothing) or run `aiko doctor` in the terminal, then report every finding and the fix it
        names. Cover the database, the access token, the daemon port, the registered projects and
        agent configurations that point at an old endpoint.
        """;

    /// <summary>
    /// Global slash command that repairs what the diagnosis found.
    /// </summary>
    public const string GlobalRepair =
        """
        Repair the local Aiko installation. Run `aiko doctor` in the terminal first to see the
        findings, then `aiko repair --fix` to reindex the projects and rewrite the agent
        configurations that point at an old endpoint; without --fix it only reports. A repair never
        deletes project files and never removes an installation - say what it changed and show the
        report it prints afterwards.
        """;

    /// <summary>
    /// Global slash command that manages agent integrations.
    /// </summary>
    public const string GlobalAgents =
        """
        Manage the Aiko integration of the agents on this machine. Run `aiko agent list` to see the
        detected agents, `aiko agent install --project <id> [--agent <ids>]` to connect one to a
        project, `aiko agent install --scope user` for the global skills and MCP entry, and
        `aiko agent uninstall ...` to remove it. Tell the user to restart the agent afterwards so it
        loads the new MCP server.
        """;

    /// <summary>
    /// Global slash command that shows the access token.
    /// </summary>
    public const string GlobalToken =
        """
        Show the local Aiko access token with `aiko token show`, or the aiko_token MCP tool. The token
        authenticates REST and MCP clients; a browser is paired once with `aiko ui` instead, so it is
        never needed in a URL. Treat it as a secret for this machine: do not paste it into issues,
        prompts or commits.
        """;

    /// <summary>
    /// Global slash command that backs up a project.
    /// </summary>
    public const string GlobalBackup =
        """
        Back up a project's `.aiko` tree with the aiko_backup MCP tool and the project id; it writes a
        timestamped zip under the Aiko data directory and returns the path. Restoring is a deliberate,
        manual step: unpack the archive yourself. Aiko never overwrites or deletes project files.
        """;

    /// <summary>
    /// Slash command that opens the local Aiko UI.
    /// </summary>
    public const string UiCommand =
        """
        Open the local Aiko UI for the current project. Use the aiko_open_ui MCP tool
        and return its local URL if the browser cannot be opened automatically.
        """;

    /// <summary>
    /// Instruction block for AGENTS.md describing Aiko as the project working memory.
    /// </summary>
    public const string ProjectInstructions =
        """
        When Aiko MCP is available, use it as the durable project workflow and task memory.
        Read project context before taking a card, report progress and preserve execution handoffs.
        Warn before modifying files outside the card scopeFiles and record actual changed files.
        """;

    /// <summary>
    /// Always-apply Cursor rule about coordination through Aiko.
    /// </summary>
    public const string CursorRule =
        """
        ---
        description: Coordinate project work through Aiko
        alwaysApply: true
        ---

        When Aiko MCP is available, read its project context before project work. Keep card
        status, progress, scope changes, actual changed files and agent handoffs synchronized.
        """;
}
