using Aiko.Application.Agents;
using Aiko.Domain.Cards;
using Aiko.Domain.Workflow;

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
    /// The skill above under another name.
    /// </summary>
    /// <remarks>
    /// A client that gives a global skill precedence over a project skill of the same name would otherwise
    /// hide this one behind <see cref="GlobalSkill"/> - Cline documents exactly that precedence - so the
    /// workspace skill is installed under its own name there. That name must match the skill's directory.
    /// </remarks>
    public static string SkillNamed(string name) =>
        Skill.Replace("name: aiko", $"name: {name}", StringComparison.Ordinal);



    /// <summary>
    /// Slash command that creates a card of a type the user names, resolved against the project's own
    /// workflows, and estimates it in the same pass.
    /// </summary>
    /// <remarks>
    /// This is the type-agnostic entry point: it reads the project context instead of naming types itself, so
    /// a project that adds a card type later needs no regenerated file for the agent to learn about it. The
    /// arguments are free text - the type first, then what the card is about - which is why the command says
    /// what to do with each of them rather than parsing a fixed signature. A card that is created but left
    /// without a size and scores is one nobody can rank, so the command estimates it in the same pass; an
    /// estimate the user typed wins over the agent's judgement.
    /// </remarks>
    public const string Create =
        """
        Create a card in Aiko. The user named a type first and then what the card is about.

        Read aiko_get_project_context first: it lists every card type this project defines, the workflow
        behind each one, the stages of its pipeline, the size grid and the scoring criteria. Use the type the
        user named; if they named none, ask which type they mean.

        Then call aiko_create_card with that type's kind, a clear title taken from what the user asked for, an
        own priority and, for work that changes files, the declared scope patterns - and nothing else. Aiko
        names the card and lands it in the type's backlog stage, so never invent a card id or a stage. Put
        what the user described into the card's requirements when it says more than the title.

        Estimate the card in the same pass, unless the user gave the numbers themselves: read the card back to
        get its revision, judge the size step whose description matches the work and every criterion the
        project defines, and write them with aiko_estimate_card.

        Report at the end: the created card id, the size and the scores you wrote, and one line on what the
        card is about.
        """;

    /// <summary>
    /// Slash command that creates a sub-card under an existing card: the same creation as
    /// <see cref="Create"/>, plus the parent-child edge and the estimate.
    /// </summary>
    /// <remarks>
    /// The edge is drawn from the parent to the child, which is the direction the board reads as "this card
    /// belongs under that one" - so the command names the parent as the source, not as the target, because
    /// getting it backwards would show the sub-card as its parent's parent.
    /// </remarks>
    public const string CreateSub =
        """
        Create a sub-card in Aiko under an existing card. The user named the parent card id first, then the
        type and then what the sub-task is about.

        Read aiko_get_project_context and aiko_get_card for the parent. The context lists every card type,
        its workflow, the size grid and the scoring criteria; the parent says what the whole thing is about,
        which is what the sub-card has to contribute to.

        Call aiko_create_card with the type's kind, a clear title, an own priority and, for work that changes
        files, the declared scope patterns - and nothing else: Aiko names the sub-card and lands it in
        backlog. Put what the user described into the sub-card's requirements when it says more than the
        title.

        Then link it to its parent: call aiko_link_cards with the PARENT card id as sourceCardId, the new
        sub-card id as targetCardId and "parent-child" as the relation type. The edge points from the parent
        to the child; the other way round the board would show the sub-card as the parent.

        Finally estimate the new card exactly as /aiko-create does - read it back for its revision, judge the
        size step and every criterion the project defines, and write them with aiko_estimate_card.

        Report at the end: the created card id, its parent, and the size and scores you wrote.
        """;

    /// <summary>
    /// Slash command for one card type of the project this file is installed into, named after that type.
    /// </summary>
    /// <remarks>
    /// Generated rather than written by hand, because the set of types is project data: the daemon re-writes
    /// these commands when a type is added or removed. The type id is the workflow id, so the command can
    /// name the pipeline it belongs to without re-reading the project.
    /// </remarks>
    /// <param name="type">The card type the command creates.</param>
    public static string CreateCard(CardTypeDescriptor type)
    {
        var kind = CardKind.FromWorkflowId(type.Id);
        var intro = string.IsNullOrWhiteSpace(type.Description)
            ? $"Create a {type.Title} card in Aiko."
            : $"Create a {type.Title} card in Aiko. {type.Description.Trim()}";
        return $"""
            {intro}

            This type is the workflow {type.Id}: call aiko_create_card with kind={kind}, a clear title taken
            from what the user asked for, an own priority and, for work that changes files, the declared
            scope patterns - and nothing else. Aiko names the card and lands it in the workflow's
            {WorkflowDefinition.BacklogStageId} stage, so never invent a card id or a stage; the project
            context lists the rest of that pipeline if you need it. Put what the user described into the
            card's requirements when it says more than the title.

            Estimate the card in the same pass, unless the user gave the numbers themselves: read it back for
            its revision, judge the size step and every criterion the project defines, and write them with
            aiko_estimate_card.

            Report at the end: the created card id, the size and the scores you wrote, and one line on what
            the card is about.
            """;
    }

    /// <summary>
    /// Slash command that estimates a card: the size step and the criterion scores the person left for the
    /// machine to judge.
    /// </summary>
    /// <remarks>
    /// Estimation is what lets a card be created from a title and its requirements alone. The grid and the
    /// criteria are project data, so the command reads them from the context instead of naming them, and the
    /// agent's judgement is written back through <c>aiko_estimate_card</c> rather than typed into the UI.
    /// </remarks>
    public const string Estimate =
        """
        Estimate a card in Aiko. The user named the card: its id is the first argument.

        Read aiko_get_project_context and aiko_get_card. The context carries the project's size grid, with
        each step's description, and every scoring criterion with its range and weight; the card carries its
        title, its requirements and its declared scope.

        Judge the work from those and call aiko_estimate_card with the card id, the revision you read and
        the estimate: the size step whose description matches the work, and one "criterionId=score" entry per
        criterion the project defines. Set the own priority only when the criteria do not already account for
        it. A step that means "too large for one stage" is a plan to split the card, not an estimate to start
        from - say so instead of writing it.

        Report at the end: the size and the scores you wrote, what you judged them from, and whether the
        card looks too large to run as it stands.
        """;

    /// <summary>
    /// Slash command that runs a card: the work its current stage asks for, whichever stage and whichever card
    /// type the project defines.
    /// </summary>
    /// <remarks>
    /// The stage is read from the card rather than named here, and that is the whole point: stages and card
    /// types are project data, so a command saying "run the implementation stage" is as wrong in a project
    /// with a different pipeline as "create a task" is in a project without tasks. Moving the card to a named
    /// stage first is the same operation, which is why this replaces the separate move command and the
    /// per-stage ones: analyze, implement and review were one instruction with a different stage name in it.
    /// </remarks>
    /// <param name="agentAdapterId">
    /// The adapter this file is installed for, written into the text. <c>aiko_start_stage</c> records which
    /// agent is responsible, and a file that belongs to one adapter already knows the answer.
    /// </param>
    public static string Run(string agentAdapterId) => $"""
        Run a card in Aiko. The user named the card: its id is the first argument, and an optional stage id
        may follow as the second.

        Read aiko_get_project_context and aiko_get_card for that card. The context lists the card's type, its
        pipeline and what each stage demands; the card says which stage it is in right now.

        If a stage id was given and is not the card's current stage, call aiko_move_card to put the card
        there first and re-read it - moving the card is the only way it changes stage.

        Then start the stage with aiko_start_stage, passing the card id, the id of the stage the card is now
        in, and "{agentAdapterId}" as the agent adapter id. Do what the stage's instruction asks for, and
        honour its beforeSkills and afterSkills. Never start a second execution for a card that already has
        one: if aiko_start_stage refuses because the card has an active execution, continue, complete or hand
        off that execution instead of starting another.

        Keep aiko_report_progress updated with the summary, the steps done and left, and the complete current
        list of the files you changed. If the work needs files outside the card's declaredScopeFiles, call
        aiko_request_scope_expansion and wait for the user's decision before touching them.

        Before you complete the stage, re-score the card with aiko_estimate_card: the project context lists
        its criteria, and the one about how complete the card is (complete, or whatever the project named
        readiness) must describe the card as it is after your change, not as it was before it. Re-score the
        importance criteria - app-point and user-point - only when the work changed what they measured.

        Finish with aiko_complete_stage, recording the files you changed, the artifacts you produced and how
        you verified the result, and keep durable conclusions with aiko_store_memory. If you cannot finish - a
        rate limit, a failure - report the state with aiko_report_agent_state and hand the execution to
        another agent with aiko_handoff_execution rather than dropping it.

        Report at the end: the card, the stage you ran, what changed, what is left, and whether the card is
        ready for its next stage.
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
    /// Name of the environment variable a client can read the daemon's access token from, for clients
    /// whose MCP configuration cannot hold a literal header (Codex reads exactly this name; the value is
    /// the shape its own <c>codex mcp add --bearer-token-env-var</c> writes).
    /// </summary>
    public const string AccessTokenEnvironmentVariable = "AIKO_TOKEN";

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
    /// <remarks>
    /// The template choice is part of the command, not an afterthought: the template decides the stages
    /// with their agent instructions and required artifacts, the board projections, the starting memory and
    /// the default settings, and changing a template later only affects projects created afterwards.
    /// </remarks>
    public const string Init =
        """
        Register the current directory as an Aiko project. The template is the whole answer to what the
        project starts as, so this command takes its id: /aiko-init <templateId>. If the user did not name
        one, call the daemon MCP tool `aiko_list_templates` and ask which to use - the template fixes the
        stages with their agent instructions and required artifacts, the board projections, the starting
        memory and the default settings, and a later change to it does not reach a project created before it.
        The project's name defaults to the directory name, and its id - the short handle the UI's URLs use,
        for example 'aiko' for /p/aiko/board - defaults to a slug derived from that name. Ask the user for
        both only if they want something other than the defaults; a name or id they give is used as-is, and
        an id that is already taken is refused.
        Then run `aiko init <path> [--name <n>] [--id <slug>] [--git-policy <p>] [--template <id>]` in the
        terminal (Aiko is installed and on PATH), or call `aiko_init_project` with the chosen templateId,
        name and projectId.
        Once the project exists, read aiko_get_project_context and carry out its project initialization
        instruction if it has one: that is what the template asks for beyond copying its files, such as the
        structure the project should have. Report what you created or changed. Then restart this agent so
        the project-scoped MCP configuration and skills are loaded.
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
