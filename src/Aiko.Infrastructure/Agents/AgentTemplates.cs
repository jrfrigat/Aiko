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
    /// The step a user-scope procedure starts with: work out which project the user has open, and what to
    /// say when there is none.
    /// </summary>
    /// <remarks>
    /// The working directory is the only thing that can say which project this is: a user-scope skill is
    /// visible in every folder, and in a client like Cline the enabled MCP entry - not the folder - is what
    /// carries a project. Asking first is what keeps a card from landing in whichever project is connected.
    /// </remarks>
    public const string ProjectPreamble =
        """
        First work out which project this is: this skill is installed for the whole machine and can be run in
        any folder, while the project to work with is the folder the user has open.

        1. Run `aiko project find "<the working directory>"` and read the answer.
        2. If it names a project, that is the one. Confirm its tools are available by reading
           `aiko_get_project_context`; when that tool is not available, the project is not connected to this
           agent - tell the user to connect it and stop (`aiko agent install --project <project>`; in Cline
           the entry `aiko-<project>` also has to be enabled in its MCP server list).
        3. If it says the folder carries .aiko but is not registered, tell the user it is not registered yet
           and that `aiko init` in that folder registers it. Do nothing else.
        4. If it says the folder is not an Aiko project, tell the user the folder has to be initialized
           first (`/aiko-init`, or `aiko init` in that folder) and stop. Never pick another project to work
           in, and never create the card somewhere else.
        """;

    /// <summary>
    /// One procedure Aiko installs into an agent: the name its skill is loaded under, the description a
    /// client matches to decide whether to load it, and the body that says what to do.
    /// </summary>
    /// <remarks>
    /// A skill carries a procedure the model may invoke on its own. The working contract is not one: it has
    /// to hold whether or not a model judges a skill relevant, so it travels in the rule channel instead
    /// (see <see cref="ProjectInstructions"/> and <see cref="CursorRule"/>). Calling the contract a skill
    /// was how the two got muddled.
    /// </remarks>
    public sealed record Procedure(string Name, string Description, string Body)
    {
        /// <summary>The skill document: the Agent Skills frontmatter plus the body.</summary>
        public string ToSkill() => $"""
            ---
            name: {Name}
            description: {Description}
            ---

            {Body.Trim()}
            """;

        /// <summary>
        /// The skill document for a user-scope install: the same procedure with the step that works out which
        /// project the user has open in front of it.
        /// </summary>
        /// <remarks>
        /// A workspace copy needs no such step - it sits in the project, and the client that reads it is
        /// configured for that project. A user-scope copy is visible in every folder, so it has to ask before
        /// it acts.
        /// </remarks>
        public string ToGlobalSkill() => $"""
            ---
            name: {Name}
            description: {Description}
            ---

            {ProjectPreamble}

            {Body.Trim()}
            """;
    }

    /// <summary>
    /// The procedures a project installs: one per workflow step, plus one create procedure per card type
    /// the project declares.
    /// </summary>
    /// <remarks>
    /// The per-type procedures are generated rather than written by hand, because the set of types is
    /// project data: the daemon re-writes them when a type is added or removed. Each one is installed
    /// through every channel its client supports - a skill where the client has skills, a slash command
    /// where it has those - so the same procedure is reachable by relevance and by name.
    /// </remarks>
    /// <param name="agentAdapterId">
    /// The adapter these files are installed for, written into the run procedure: <c>aiko_start_stage</c>
    /// records which agent is responsible, and a file that belongs to one adapter already knows the answer.
    /// </param>
    /// <param name="cardTypes">Card types the project declares.</param>
    public static IReadOnlyList<Procedure> ProjectProcedures(
        string agentAdapterId,
        IReadOnlyList<CardTypeDescriptor> cardTypes) =>
    [
        new(
            "aiko-create",
            "Create an Aiko card of any type the project defines, and estimate it in the same pass.",
            Create),
        new(
            "aiko-create-sub",
            "Create an Aiko sub-card under an existing card, link it to its parent and estimate it.",
            CreateSub),
        .. cardTypes.Select(type => new Procedure(
            $"aiko-create-{type.Id}",
            $"Create an Aiko {type.Title} card and estimate it in the same pass.",
            CreateCard(type))),
        new(
            "aiko-estimate",
            "Estimate an Aiko card - judge the size step and every scoring criterion the project defines.",
            Estimate),
        new(
            "aiko-run",
            "Run an Aiko card - do what its current stage asks for, report progress and complete the stage.",
            Run(agentAdapterId)),
        new(
            "aiko-scope",
            "Request a scope expansion for an Aiko card whose work needs files outside its declared scope.",
            Scope),
        new(
            "aiko-handoff",
            "Hand an Aiko stage execution to another agent without losing its history.",
            Handoff),
        new(
            "aiko-memory",
            "Search and store the durable Aiko project memory.",
            Memory),
        new(
            "aiko-status",
            "Summarize the Aiko work in progress.",
            Status),
        new(
            "aiko-ui",
            "Open the Aiko board for this project.",
            UiCommand),
    ];



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

        Finally estimate the new card exactly as the aiko-create procedure does - read it back for its
        revision, judge the size step and every criterion the project defines, and write them with
        aiko_estimate_card.

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
        Run a card in Aiko. The user named the card: its id is the first argument, an optional stage id may
        follow as the second, and --all may follow as the last one.

        Read aiko_get_project_context and aiko_get_card for that card. The context lists the card's type, its
        pipeline and what each stage demands; the card says which stage it is in right now.

        Run ONE stage and stop: the user asked for this stage, and asking for the next one is theirs to do.
        Start it with aiko_start_stage, passing the card id, the id of the stage you mean to work, and
        "{agentAdapterId}" as the agent adapter id. The start moves the card into that stage, so a card may
        still be sitting in its backlog when you begin - and until that start, the card has no work in it:
        do not create or change any file for it beforehand. A stage that is not finished is the stage you
        work: starting it again continues that same run, and starting a different one is refused until it is
        done.

        If the user passed --all, do not stop between stages: after aiko_complete_stage, start the next stage
        of the pipeline and keep going until it ends, filling in every card as you go (progress, artifacts and
        the re-score below). Even then, stop and tell the user when a stage asks them a question, when an agent
        fails or hits its rate limit, when the stage's policy forbids an action, or when a required artifact
        cannot be produced.

        Do what the stage's instruction asks for and honour its beforeSkills and afterSkills. Produce the
        artifacts the stage requires, because they are what the stage is judged by. Complete the stage with
        aiko_complete_stage once its instruction and artifacts are done - the card moves on from there, and
        a card whose stage was never run cannot move on at all.

        Keep aiko_report_progress updated with the summary, the steps done and left, and the complete current
        list of the files you changed. If the work needs files outside the card's declaredScopeFiles, call
        aiko_request_scope_expansion and wait for the user's decision before touching them.

        Post the outcome of the stage into the card's own feed with aiko_add_comment - what you did, what you
        found and what is left - so the card explains what came of it instead of carrying an empty discussion.
        Read aiko_list_comments first when the card already has one, and answer what is there.

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
    /// The init command as one adapter receives it: the shared text plus the identifier that connects that very
    /// agent to the project it creates.
    /// </summary>
    /// <remarks>
    /// Aiko knows which adapter a file was generated for and the agent reading it does not have to say, so the
    /// id is written in. Naming it makes init connect the agent in the same step, which is what a user means by
    /// running init from inside an agent; a project created from the UI picks its agents on the form instead.
    /// </remarks>
    /// <param name="agentAdapterId">Adapter id, for example <c>claude-code</c>.</param>
    public static string InitFor(string agentAdapterId) =>
        $"""
        {Init}

        When this runs from inside an agent, name that agent so it is connected to the project in the same
        step: `aiko init <path> --agent {agentAdapterId}`. The step is idempotent - an agent that is already
        connected is reported as such instead of failing.
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
    /// <remarks>
    /// The first sentences are the ones that matter: work the user asks for starts with a card, and a card's
    /// work happens inside a stage execution. A contract that only said "start with a card" was satisfied by
    /// creating one and then editing files with the card still in the backlog - a card that then looks worked
    /// while its runs tab, artifacts and history stay empty. Naming the start is what closes that gap.
    /// </remarks>
    public const string ProjectInstructions =
        """
        When Aiko MCP is available, use it as the durable project workflow and task memory. Any work the user
        asks for starts with a card: create it in Aiko first (aiko_create_card, or /aiko-create), and stop
        there - the card is the answer to the request, and the work begins when the user asks for it (aiko-run,
        or "выполни"). A card in its backlog stage has no work in it yet, so never create or change files for a
        card before you have started the stage you are working in with aiko_start_stage - the start moves the
        card into that stage and is what records the work. Do what the stage's instruction asks for, produce
        its required artifacts and complete it with aiko_complete_stage; only then does the card move on, and
        aiko_move_card refuses to advance a card whose stage is not finished. Before you complete a stage,
        re-estimate the card with aiko_estimate_card: the readiness criterion is what says the work is done,
        and it must describe the card as it is after your change - a stage is not completed with a score
        nobody refreshed. One run is one stage: work the next stage only when the user asks again, and
        /aiko-run <cardId> --all is the explicit exception - it
        walks the pipeline, and even then it stops when a stage asks the user a question, when an agent fails or
        hits its limit, when a stage forbids an action, or when a required artifact cannot be produced. Read the
        project context before touching files. Warn before modifying files outside the card scopeFiles and
        record the actual changed files.
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

        When Aiko MCP is available, read its project context before project work. Any work the user asks for
        starts with a card: create it in Aiko first and stop there - the work begins when the user asks for it.
        Then start the stage you are working in with aiko_start_stage - a card in its backlog has no work in it,
        so no file is created or changed for it before that start. Before you complete a stage, re-estimate the
        card with aiko_estimate_card - the readiness criterion must describe the card as it is after the work.
        One run is one stage: after
        aiko_complete_stage stop, unless the user asked for --all, which walks the pipeline and still stops on a
        question to the user, a failure or a forbidden action. Produce the stage's artifacts and keep card
        status, progress, scope changes, actual changed files and agent handoffs synchronized.
        """;
}
