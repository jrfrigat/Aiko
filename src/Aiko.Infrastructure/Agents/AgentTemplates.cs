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
            Create + CreateStopsHere),
        new(
            "aiko-create-sub",
            "Create an Aiko sub-card under an existing card, link it to its parent and estimate it.",
            CreateSub + CreateStopsHere),
        .. cardTypes.Select(type => new Procedure(
            $"aiko-create-{type.Id}",
            $"Create an Aiko {type.Title} card and estimate it in the same pass.",
            CreateCard(type) + CreateStopsHere)),
        new(
            "aiko-estimate",
            "Estimate an Aiko card - judge the size step and every scoring criterion the project defines.",
            Estimate),
        new(
            "aiko-run",
            "Run an Aiko card - do what its current stage asks for, report progress and complete the stage.",
            Run(agentAdapterId)),
        new(
            "aiko-run-all",
            "Work the Aiko board - every unfinished card in board order, each driven to the end of its pipeline.",
            RunAll(agentAdapterId)),
        new(
            "aiko-commands",
            "Carry out the commands the Aiko board placed for an agent - start, pause, resume or answer a stage.",
            Commands(agentAdapterId)),
        new(
            "aiko-link",
            "Link this Aiko project to another one and say what that project is for.",
            Link),
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
    /// What every create procedure says at the end: the card is the whole answer to the request.
    /// </summary>
    /// <remarks>
    /// A create procedure can be the only text an agent reads - a model that loads a skill by relevance never
    /// sees the rule channel - and a procedure ending at "estimate it" reads as "now do the work". That is how
    /// a request turned into a finished task nobody had asked to run. The tail is written once here and
    /// appended where the procedures are assembled, so each body stays as authored while every channel - a
    /// skill, a command, a user-scope install - ends with the same line.
    /// </remarks>
    private const string CreateStopsHere =
        "\n\n" + """
        Then stop. Creating the card, and estimating it, is the whole answer to what the user asked: do not
        start a stage, do not create or change files for the card, and do not begin the work - the user starts
        it with aiko-run <cardId> or "выполни <cardId>".
        """;

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
        names the card and lands it in the type's backlog stage, so never invent a card id or a stage.

        Give the card both of its texts. Put the user's own wording into request - verbatim, or as close as you
        can get it without tidying it up into a task: the request records what was asked for, and the card stops
        being able to change it once it leaves the backlog. Put what the card is asked to do into requirements
        when it says more than the title: that one describes the work, and it changes as the work is understood.

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
        title, and the user's own wording into request.

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
            context lists the rest of that pipeline if you need it. Put the user's own wording into request and
            what the card is asked to do into requirements when the title alone is not enough.

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
    /// Slash command that works the project's board: every unfinished card, in board order, driven to the
    /// end of its own pipeline.
    /// </summary>
    /// <remarks>
    /// The pass is a procedure rather than an engine, because Aiko does not run agent processes: what an
    /// agent needs is the algorithm, and the algorithm is this text. Its progress is not stored anywhere
    /// either - the board already says it, and a second copy of "where the pass got to" is the thing that
    /// would sooner or later disagree with the runs. What the pass must not do is duplicate a rule that
    /// already has a gate: blocked cards are skipped by reading the refusal of <c>aiko_start_stage</c>, not
    /// by walking the blocking graph here.
    /// </remarks>
    /// <param name="agentAdapterId">
    /// The adapter this file is installed for, written into the text: every stage the pass starts records
    /// which agent is responsible, and a file that belongs to one adapter already knows the answer.
    /// </param>
    public static string RunAll(string agentAdapterId) => $"""
        Work the project's board: take the cards whose pipeline is unfinished, in board order, and drive
        each one to the end of its own workflow. This is what the board's "work the board" button asks for,
        and what the user means by running the board.

        Read aiko_get_project_context for the card types and their pipelines, and aiko_list_board for the
        cards, where each stage got to, and the priority the board shows them with. That order is the
        board's own - the computed priority, then the card id - and not the card's own score, so read it
        from the board rather than sorting the cards yourself.

        For each card, in that order:

        - Skip a card that is already finished: it sits in the last stage of its own workflow with a
          completed run. Skip a card that waits for another one in the same way - aiko_start_stage refuses it
          and names the blocking card, and that refusal is the skip. Do not walk the blocking graph
          yourself: asking and reading the answer is the one place that rule lives.
        - Work each unfinished stage as /aiko-run does: aiko_start_stage with "{agentAdapterId}", do what
          the stage's instruction asks, produce the artifacts it requires, report progress, re-estimate the
          card with aiko_estimate_card, write the outcome into the card's feed with aiko_add_comment, and
          complete it with aiko_complete_stage. Then start the next stage of that card's own pipeline.
        - The card is done when its last stage's run is completed. Then take the next card.

        When a question for the person comes up on a card, do not stop the pass: write the question into
        that card's feed with aiko_add_comment, put its run into the waiting state with
        aiko_report_agent_state and "waiting-for-user", and take the next card. That card is not lost - it
        is exactly where the person will look for it.

        Stop the whole pass only when you cannot go on at all: the agent fails or hits its rate limit, a
        policy forbids the action (a refused scope expansion, a commit or push the project denies), or a
        required artifact cannot be produced. Everything else - a blocked card, a card waiting for an
        answer, a card someone else has already finished - is a reason to move on, not to stop.

        The pass needs no bookkeeping to be resumable: run it again and it starts from the board as it now
        is. Finished cards are not picked again; a card waiting for an answer is skipped because
        aiko_start_stage refuses to restart an unfinished stage; and a card the pass stopped on is picked up
        by the same start, which continues that run instead of opening a second one.

        The state of the project is read and written through these tools. Do not open a file under .aiko to
        find out what the project says: read the order with aiko_list_work_queue, a card with aiko_get_card,
        what sits beside it with aiko_get_card_artifact, the settings with aiko_get_settings. Produce a stage's
        artifacts with aiko_save_card_artifact rather than by writing the file yourself - that is Aiko's own
        data, and a document written past the daemon is a document the daemon does not know about. A card whose
        subject is the .aiko format itself is the exception: there the file is the work.

        Do not push and do not open branches: the shared checkout is the user's, and the git, commit and
        push policies the project states say who may write to it.

        Report at the end: which cards you worked and what changed, which you skipped and why, which are
        waiting for an answer, and what is left. If an aiko_list_commands and aiko_claim_command pass
        brought you here - the command's action reads "RunBoard" - close that command with
        aiko_finish_command when the pass ends: "completed" with the summary, or "failed" with the reason it
        stopped. Never leave it open, because the queue is what the person reads to see whether their
        request happened.
        """;

    /// <summary>
    /// Slash command that carries out the commands a screen placed for an agent.
    /// </summary>
    /// <remarks>
    /// Aiko does not run an agent process, so a request that starts on the board waits in the project's own
    /// queue until an agent is there to take it. This procedure is what empties that queue: without it the
    /// button places a command nobody ever reads, which is the failure mode the whole channel exists against.
    /// </remarks>
    /// <param name="agentAdapterId">
    /// The adapter this file is installed for, written into the text: a command names the agent it was placed
    /// for, and a file that belongs to one adapter already knows whether it may take that command.
    /// </param>
    public static string Commands(string agentAdapterId) => $"""
        Carry out what the Aiko board asked this project's agents to do. The person pressed a button while
        no agent was running, so the request has been waiting in the project's queue since.

        Read the queue with aiko_list_commands (state "open"). Take one command with aiko_claim_command,
        passing "{agentAdapterId}" as the agent adapter id. A refusal is not a failure: it means another
        agent already has that command, or the person placed it for a different agent - say so and take the
        next one rather than insisting.

        Do what the command's action asks, with the tool that already does it:

        - start - aiko_start_stage with the command's cardId and stageId, and your own adapter id. Then work
          that stage exactly as /aiko-run does: the same rules about one stage per run, the card's feed, the
          artifacts the stage requires, the re-score before completing it, and no push.
        - pause - aiko_pause_execution for the command's executionId, with the command's text as the reason.
        - resume - aiko_resume_execution for the command's executionId.
        - answer - write the command's text into the card's feed with aiko_add_comment, then call
          aiko_resume_execution, so the run that is waiting for that answer reads it and continues.
        - work the board - a command whose action reads "RunBoard" names no card: it asks for the whole
          board. Run the /aiko-run-all procedure and let the board decide the order and what is left.

        Close the command with aiko_finish_command: completed when the work the command asked for happened,
        failed when it could not be, with a message naming which. Never leave a taken command open - the
        person reads the queue to see whether their request happened, and a command that stays open after
        the work is done is worse than having no queue at all.

        Report at the end: which commands you took, what each asked for, what you did with them, and what is
        still waiting.
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
        pipeline and what each stage demands; the card says which stage it is in right now, and which cards
        block it.

        A card that waits for another one is not yours to start: aiko_start_stage refuses it and names the
        blocking card. Do not look for a way around that - say so to the user and offer the blocking card
        instead, because finishing it is what unblocks the work they asked for.

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

        --all descends into the card's children when the card is a container - an epic holds its goal across
        stories, a story across tasks, and the stage that asks for the breakdown is the stage that gets it.
        Create the children a stage calls for with aiko_create_card and link each one to its parent with
        aiko_link_cards ("parent-child"). A created child is not stage work, so creating it costs nothing; when
        a child has to be worked, drive it to the end of its own pipeline - the same start, artifacts,
        re-estimate, feed note and complete - and go back to its parent only afterwards. The children and the
        order to take them in come from the parent's own statement on its card; the board's priority is not
        that order.

        The whole tree shares one run slot. Aiko counts the runs whose state is Running against the project's
        maxConcurrentRuns, and a run that is parked, waiting or needs attention holds no slot, so a parent left
        running refuses its own child's start. Park the parent with aiko_pause_execution, the reason naming the
        child you are descending into, work the child, then continue the parent with aiko_resume_execution and
        finish the stage it was in. Park a stage that is still open: a stage that already has a completed run
        reads as completed on the board, and parking it would hide that the card is waiting - do not do it.
        Never leave a parent parked and forgotten: a stop names the card it stopped on and says which parent
        was left parked and why.

        A child that cannot be worked stops the pass: aiko_start_stage refusing it because another card blocks
        it, a question to the user, a failure or a rate limit, a forbidden action. The parent's stage cannot be
        completed honestly without its child, so do not skip it and do not complete the parent around it.

        Do what the stage's instruction asks for and honour its beforeSkills and afterSkills. Produce the
        artifacts the stage requires, because they are what the stage is judged by. Complete the stage with
        aiko_complete_stage once its instruction and artifacts are done - the card moves on from there, and
        a card whose stage was never run cannot move on at all.

        Keep aiko_report_progress updated with the summary, the steps done and left, and the complete current
        list of the files you changed. If the work needs files outside the card's declaredScopeFiles, call
        aiko_request_scope_expansion and wait for the user's decision before touching them.

        Do not push and do not open branches: the shared checkout is the user's, and the git, commit and push
        policies the project states in aiko_get_project_context say who may write to it. Aiko has no push of its
        own, so nothing enforces this but the rule itself - follow it.

        Read the card's feed with aiko_list_comments before you start the work: the stages before you leave
        notes there - what they learned, what the user asked for, what they left for you - and the feed is the
        only place that survives a stage. Post the outcome of the stage with aiko_add_comment before you
        complete it - what you did, what you found, what is left and what the next stage or agent will need -
        and sign it with your own adapter id as the author, so the feed says which agent wrote what. Answer what
        is already there rather than repeating it.

        Finish with aiko_complete_stage, recording the files you changed, the artifacts you produced and how
        you verified the result, and keep durable conclusions with aiko_store_memory. If you cannot finish - a
        rate limit, a failure - report the state with aiko_report_agent_state and hand the execution to
        another agent with aiko_handoff_execution rather than dropping it.

        The state of the project is read and written through these tools. Do not open a file under .aiko to
        find out what the project says: read the order with aiko_list_work_queue, a card with aiko_get_card,
        what sits beside it with aiko_get_card_artifact, the settings with aiko_get_settings. Produce a stage's
        artifacts with aiko_save_card_artifact rather than by writing the file yourself - that is Aiko's own
        data, and a document written past the daemon is a document the daemon does not know about. A card whose
        subject is the .aiko format itself is the exception: there the file is the work.

        Report at the end: the card, the stage you ran, what changed, what is left, and whether the card is
        ready for its next stage.
        """;

    /// <summary>
    /// Slash command that links this project to another one, saying what the neighbour is for.
    /// </summary>
    /// <remarks>
    /// The description is the whole value of a link: an agent reads it before deciding whether a piece of work
    /// belongs to the neighbour, so the procedure asks for the user's words rather than for a label.
    /// </remarks>
    public const string Link =
        """
        Link this project to another one in the Aiko registry, so work that belongs to the neighbour can be
        filed there instead of here.

        Ask the user which project and what it is for. The description is what a later agent reads to decide
        when the neighbour is the right place for a piece of work, so it is written in the user's own words, as
        a sentence about that project - not as a label like "other project".

        Call aiko_list_projects to find the project by its readable handle, then aiko_link_project with the
        project whose registry is written, the project to link, and that description. Linking a project to
        itself is refused, and so is linking one nobody has registered. A link can be removed with
        aiko_unlink_project: that stops future routing and leaves the cards already filed there alone.

        The links of the current project are listed by aiko_get_project_context, so an agent working here sees
        them without asking.
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
        manage the agents' integrations (/aiko-agents), read and change settings (/aiko-settings),
        show the access token (/aiko-token), back a project up (/aiko-backup), read the log tail
        (/aiko-logs) and open the UI (/aiko-ui). After /aiko-init, restart this agent so the
        project-scoped MCP configuration and skills are loaded.

        If the Aiko tools are missing, check that AIKO_TOKEN is set to the value of `aiko token show` - a
        client that cannot store an authorization header reads the credential from that variable - and
        restart this agent so it is read.
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
        findings, then `aiko repair --fix` to reindex the projects, file the cards of projects made
        before they moved under .aiko/workflows, and rewrite the agent configurations that point at an
        old endpoint; without --fix it only reports. A repair never deletes project files and never
        removes an installation - say what it changed and show the report it prints afterwards.
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
    /// Global slash command that reads and writes settings.
    /// </summary>
    /// <remarks>
    /// The two targets are stated apart on purpose: a project's own document is what it runs with, a
    /// template's is the defaults a new project is created from, and a template never reaches a project that
    /// already took its copy. A skill that blurred them would repeat the drift §24 of the specification had
    /// to explain away.
    /// </remarks>
    public const string GlobalSettings =
        """
        Read and change Aiko settings. Read with the aiko_get_settings MCP tool: pass a project id for a
        project, or omit it for the built-in defaults a screen with no project open falls back to.
        Write with aiko_update_settings, naming exactly one target:
        - `projectId` edits the project's own settings - what that project runs with, and what its Settings
          screen shows. Send the whole document as JSON (the execution, priority and crossProject sections
          you mean to state), not a fragment: a section you state is written as it stands, so a field left
          out of it falls back to its type's default.
        - `templateId` edits the defaults a new project starts from. This never changes a project that
          already exists - its settings were copied when it was created and are its own from then on - so
          say that to the user before writing a template.
        The answer names the source of each value (the project or the built-in default): report it instead
        of assuming the write landed. There is no installation-level settings document.
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
    /// Global slash command that shows the tail of the daemon log, and of a project's event journal.
    /// </summary>
    /// <remarks>
    /// The daemon log is the only place that says why a daemon stopped by itself, and the journal is the
    /// only place that says what happened to a project's cards and stage runs. Both were reachable only by
    /// knowing the command, so the skill names it and the flags.
    /// </remarks>
    public const string GlobalLogs =
        """
        Show what the Aiko installation and a project have been doing. Run `aiko logs` in the terminal for
        the tail of the daemon's own log - what it said on its way out, which is how "it stopped by itself"
        is answered - with `--lines <n>` for more of it. Add `--project <id>` for the tail of that project's
        event journal: what happened to its cards and its stage runs. An empty log says so rather than
        failing, and the log is bounded, so it also says what it dropped.
        """;

    /// <summary>
    /// Slash command that opens the local Aiko UI.
    /// </summary>
    public const string UiCommand =
        """
        Open the local Aiko UI for the current project. Use the aiko_open_ui MCP tool
        and return its local URL - the project board, or the card it names - if the browser cannot be
        opened automatically.
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
        there - the card is the answer to the request. An order is still a request: "поправь ...", "исправь ..."
        and "нужно ..." earn a card and nothing more, and the work begins only when the user asks for it
        (aiko-run <cardId>, or "выполни <cardId>"). A card in its backlog stage has no work in it yet, so never
        create or change files for a
        card before you have started the stage you are working in with aiko_start_stage - the start moves the
        card into that stage and is what records the work. A card may wait for another one: aiko_get_card lists
        the cards that block it, and aiko_start_stage refuses a blocked card, naming the blocker. When that
        happens, do not work this card - say so to the user and offer the blocking card instead. Do what the
        stage's instruction asks for, produce
        its required artifacts and complete it with aiko_complete_stage; only then does the card move on, and
        aiko_move_card refuses to advance a card whose stage is not finished. Before you complete a stage,
        re-estimate the card with aiko_estimate_card: the readiness criterion is what says the work is done,
        and it must describe the card as it is after your change - a stage is not completed with a score
        nobody refreshed. The card's feed is the notebook between stages: read it with aiko_list_comments before
        you work a stage, and post the outcome with aiko_add_comment - signed with your adapter id - before you
        complete it, so what one stage learned is not lost on the next. One run is one stage: work the next
        stage only when the user asks again, and
        /aiko-run <cardId> --all is the explicit exception - it
        walks the pipeline and descends into the card's children, creating the ones a container's stage calls
        for and working each child before going back to its parent, and even then it stops when a stage asks the
        user a question, when an agent fails or hits its limit, when a stage forbids an action, or when a
        required artifact cannot be produced. Read the
        project context before touching files. Warn before modifying files outside the card scopeFiles and
        record the actual changed files. A project can be linked to other projects: aiko_get_project_context
        lists them with what each one is for. When a request belongs to a linked project, file it there with
        aiko_create_card_in_project and pass originProjectId and originCardId so the receiving card remembers
        where it came from - and read that project's context first, because its card types, its stages and its
        rules are its own.
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
        An order is still a request: it earns a card, not a run.
        The card's feed is the notebook between stages: read it with aiko_list_comments before you work a stage,
        and post the outcome with aiko_add_comment before you complete it, signed with your adapter id.
        Then start the stage you are working in with aiko_start_stage - a card in its backlog has no work in it,
        so no file is created or changed for it before that start. A card another card blocks is not yours to
        start: aiko_get_card lists the blockers and aiko_start_stage refuses it, naming them - tell the user and
        offer the blocking card instead of working around it. Before you complete a stage, re-estimate the
        card with aiko_estimate_card - the readiness criterion must describe the card as it is after the work.
        One run is one stage: after
        aiko_complete_stage stop, unless the user asked for --all, which walks the pipeline and descends into
        the card's children, creating the ones a container's stage calls for and working each child before
        returning to its parent, and still stops on a question to the user, a failure or a forbidden action.
        Produce the stage's artifacts and keep card
        status, progress, scope changes, actual changed files and agent handoffs synchronized.
        """;
}
