using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Aiko.Server.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Prioritization;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Storage;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools describing the current project: the card types it defines, context for planning work and the
/// UI URL.
/// </summary>
[McpServerToolType]
internal sealed class ProjectContextTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IProjectDefinitionStore definitions,
    IProjectGitPolicyReader gitPolicies,
    IProjectLinkStore links,
    IAppSettingsService settings) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(
        Name = "aiko_get_project_context",
        Title = "Get Aiko project context")]
    [Description(
        "Call this first. Returns the current project, every card type it defines with the stages of its "
        + "pipeline, the projects it is linked to with where each neighbour's reference lives, how its cards "
        + "are scored and sized, and durable-memory guidance. The answer arrives as one content block per "
        + "section, so a client that drops a long answer in the middle still shows the rest; pass section to "
        + "read one of them on its own.")]
    public async Task<CallToolResult> GetProjectContextAsync(
        [Description(
            "Optional section to return on its own: rules, git, links, scoring, types, release or "
            + "initialization. Omit it for the whole document.")]
        string? section = null,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(project.Id, cancellationToken);
        var execution = await settings.GetEffectiveExecutionAsync(project.Id, cancellationToken);
        // The scheme a release of this project follows, resolved the way every other setting is: the project's
        // own choice wins, the template's is what it started from, and the shipped ordinary scheme answers for
        // a choice that names nothing. The body travels with it, so an agent asked to conduct a release reads
        // the project's own steps instead of inventing them.
        var release = await settings.GetEffectiveReleaseAsync(project.Id, cancellationToken);
        // The git policy lives in the project's own manifest: without it an agent cannot tell whether this
        // project's .aiko is tracked, and a project whose manifest moved answers "unknown" rather than a guess.
        var gitPolicy = await gitPolicies.ReadAsync(project, cancellationToken);
        // Read from the project's own workflows rather than from a fixed pair of files: the set of card types
        // is project data, and an agent that never learns about a type cannot create one.
        var workflows = (await definitions.ReadAsync(project.Id, cancellationToken)).Workflows;
        var initialization = await DescribeInitializationAsync(project.RootPath, cancellationToken);
        // The projects this one hands work to: an agent that cannot see them files the neighbour's work here,
        // and the link registry exists precisely so that does not happen.
        var linked = await links.ListAsync(project.Id, cancellationToken);

        // One block per section, in the order the document reads: a client that truncates a long answer drops
        // its middle, and the middle is where the links live. The section list is the answer; the words of the
        // first one are written below it.
        return RenderSections(
            [
                (RulesSection, DescribeRules(project)),
                (GitSection, DescribeGit(
                    gitPolicy,
                    execution.SharedCheckoutCommitPolicy,
                    execution.SharedCheckoutPushPolicy)),
                (LinksSection, DescribeLinkedProjects(linked)),
                (ScoringSection, DescribeScoring(priority)),
                (TypesSection, DescribeCardTypes(workflows)),
                (ReleaseSection, DescribeRelease(release)),
                (InitializationSection, initialization)
            ],
            section);

        // The document's own head: what this project is, and the working contract that holds whatever else an
        // agent has read. It takes the resolved project rather than reading it again, so the header cannot
        // disagree with the sections beside it.
        static string DescribeRules(RegisteredProject project) => $"""
            # Aiko project context

            Project: {project.Name}
            Project ID: {project.Id}
            Root: {project.RootPath}

            An order is still a request: "поправь X" earns a card, not a run, and the run begins when the user
            asks for it (aiko-run <cardId> or "выполни <cardId>"). Work happens inside a stage execution, not
            beside the pipeline. A card in its backlog stage has no
            work in it yet: do not create or change files for a card before you have started the stage you are
            working in with aiko_start_stage - the start moves the card into that stage and leaves the record
            of the work. Do what the stage's instruction asks for, produce its required artifacts, and complete
            it with aiko_complete_stage; only then does the card move on. aiko_move_card advances a card one
            stage at a time and refuses to leave a stage that is not finished, because work done outside a stage
            leaves no execution, no artifacts and no history. Before you complete a stage, re-estimate the card
            with aiko_estimate_card: the readiness criterion is what says the work is done, and a stage is not
            completed while that score still describes the card as it was before the work. A card may wait for
            another one: aiko_get_card lists the cards that block it, and aiko_start_stage refuses a blocked card
            and names the blocker - do not work around that, tell the user and offer the blocking card instead.
            One run is one stage:
            after you complete a stage, stop and wait to be asked for the next one - unless the user passed
            --all, which walks the pipeline and descends into the card's children: it creates the ones a
            container's stage calls for and works each child to the end of its own pipeline before returning to
            its parent, parking the parent's run so the whole tree keeps one run slot. It still stops on a
            question to the user, a failure or a forbidden action.

            The card's feed is the notebook one stage leaves for the next: read it with aiko_list_comments
            before you start the stage - it may hold what an earlier stage learned, what the user asked for, or
            a note left for you - and post the outcome with aiko_add_comment before you complete it, including
            what the next stage or agent will need. Sign with your own adapter id as the author, so the feed
            says which agent wrote what.

            Before changing files, read the selected card and its current stage instruction.
            A stage's beforeSkills are what to invoke before you read its instruction, and its afterSkills
            are what to invoke once the instruction is done.
            Treat declaredScopeFiles as guidance. Warn before intentionally changing files outside it,
            and report actualChangedFiles when completing work.
            Use aiko_store_memory for durable decisions, conventions and lessons.

            Before you reach for something a linked project owns - a library, a component, a pattern - read the
            reference its link names (aiko_list_links) and search this project's memory first: the reference is
            recorded there precisely so that an agent does not have to guess where the neighbour's documentation
            lives, and a package installed here is not the same thing as its reference.

            The state of the project is read and written through these tools - the board and the work queue
            with aiko_list_board and aiko_list_work_queue, the cards with aiko_list_cards and aiko_get_card,
            what sits beside a card with aiko_get_card_artifact and aiko_save_card_artifact, the settings with
            aiko_get_settings, the links with aiko_list_links, the discussion with aiko_list_comments, the
            queue of requests with aiko_list_commands, the memory with aiko_search_memory. Do not open a file
            under .aiko to find out what the project says: the tools exist so that what you act on is what the
            daemon knows, and so that a document you edit is the document a person sees. A card whose subject
            is the .aiko format itself is the exception - there the file is the work, and the card says so.

            """;
    }

    /// <summary>The section id of the document's head and working contract.</summary>
    private const string RulesSection = "rules";

    /// <summary>The section id of the git and commit policies.</summary>
    private const string GitSection = "git";

    /// <summary>The section id of the projects this one hands work to.</summary>
    private const string LinksSection = "links";

    /// <summary>The section id of the scoring criteria and the size grid.</summary>
    private const string ScoringSection = "scoring";

    /// <summary>The section id of the card types this project defines.</summary>
    private const string TypesSection = "types";

    /// <summary>The section id of the release scheme this project follows.</summary>
    private const string ReleaseSection = "release";

    /// <summary>The section id of the instruction this project's template asked for.</summary>
    private const string InitializationSection = "initialization";

    /// <summary>The value that asks for the whole document, which is also what an absent section means.</summary>
    private const string WholeDocument = "all";

    /// <summary>The sections the context tool answers to, in the order the document reads them.</summary>
    private static readonly string[] SectionIds =
    [
        RulesSection,
        GitSection,
        LinksSection,
        ScoringSection,
        TypesSection,
        ReleaseSection,
        InitializationSection
    ];

    [McpServerTool(Name = "aiko_list_links", Title = "List linked Aiko projects")]
    [Description(
        "Lists the projects this one is linked to, with what each is for, where its reference lives and when "
        + "work belongs there. Read this rather than the whole project context when the neighbours are what you "
        + "need: it is a few lines, so no part of it is lost to a truncated answer, and it can be read again at "
        + "any point of a run.")]
    public async Task<string> ListLinksAsync(CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(cancellationToken);
        var linked = await links.ListAsync(project.Id, cancellationToken);
        return JsonSerializer.Serialize(linked, ServerJsonContext.Default.IReadOnlyListProjectLink);
    }

    /// <summary>
    /// The context tool's answer: one block per section, or the one section the caller named.
    /// </summary>
    /// <remarks>
    /// A block per section rather than one long text, because a client that truncates a long answer drops its
    /// middle - and the middle is where the links live, the one part an agent cannot work without. A section
    /// named by the caller is answered even when it is empty: asking a question deserves the answer "none".
    /// Left out of the whole document instead, an empty section stays silent, which is what standing alone
    /// looks like on every project that has no links.
    /// </remarks>
    /// <param name="sections">The sections in the order the document reads.</param>
    /// <param name="section">The section asked for, or null for the whole document.</param>
    private static CallToolResult RenderSections(
        IReadOnlyList<(string Id, string Text)> sections,
        string? section)
    {
        if (string.IsNullOrWhiteSpace(section) ||
            StringComparer.OrdinalIgnoreCase.Equals(section.Trim(), WholeDocument))
        {
            return Blocks(sections
                .Select(item => item.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray());
        }

        var id = section.Trim().ToLowerInvariant();
        var chosen = sections
            .Where(item => StringComparer.Ordinal.Equals(item.Id, id))
            .ToArray();
        if (chosen.Length == 0)
        {
            return Error(
                $"Unknown section \"{section.Trim()}\". The sections are {string.Join(", ", SectionIds)}; "
                + $"\"{WholeDocument}\" or no section at all returns the whole document.");
        }

        return Blocks(
        [
            string.IsNullOrWhiteSpace(chosen[0].Text) ? EmptySection(chosen[0].Id) : chosen[0].Text
        ]);
    }

    /// <summary>
    /// The words for a section that has nothing to say, when the caller asked for it by name: "none" is an
    /// answer to a question, and a silent result would read as a failure.
    /// </summary>
    private static string EmptySection(string sectionId) => sectionId switch
    {
        LinksSection =>
            "No linked projects: this project hands work to nobody. A neighbour is linked with aiko_link_project.",
        InitializationSection =>
            "No initialization instruction: the template this project was created from asks for nothing.",
        _ => "Nothing to report in this section."
    };

    /// <summary>One result carrying the blocks, in order.</summary>
    private static CallToolResult Blocks(IReadOnlyList<string> blocks) =>
        new() { Content = [.. blocks.Select(text => new TextContentBlock { Text = text })] };

    /// <summary>
    /// A result the caller is meant to read and retry from: an error of the tool rather than a protocol
    /// failure, so the message can say which sections exist.
    /// </summary>
    private static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    /// <summary>
    /// The project's own copy of what its template asked for before work starts, or an empty string.
    /// </summary>
    /// <remarks>
    /// Read from the project rather than from the template it came from: the project owns the copy, so editing
    /// a template never changes what an existing project was asked to become. A project created from a
    /// template without an instruction has no file, and then this contributes nothing at all.
    /// </remarks>
    private static async ValueTask<string> DescribeInitializationAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = AikoProjectPaths.InitializationDocument(projectRoot);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var instruction = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        return instruction.Length == 0
            ? string.Empty
            : $"""

            ## Project initialization instruction

            The template this project was created from asks for the following before work starts. Carry it out
            if it is not done yet, report what you created or changed, and do nothing when the project already
            matches - then say so.

            {instruction}
            """;
    }

    /// <summary>
    /// The card types the project defines, one section per type, each with the stages of its pipeline.
    /// </summary>
    /// <remarks>
    /// A card type is the workflow of the same name, so this is the project's own pipelined data rendered for
    /// an agent: it is what makes a type the user added in the workflow editor usable without a code change.
    /// </remarks>
    /// <param name="workflows">The project's workflows, each one a card type.</param>
    private static string DescribeCardTypes(IReadOnlyList<WorkflowDefinition> workflows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Card types of this project");
        builder.AppendLine();
        if (workflows.Count == 0)
        {
            builder.AppendLine("No workflow is defined, so this project has no card type yet.");
            return builder.ToString().TrimEnd();
        }

        builder.Append("A card's kind is the type id below and its workflowId is the same id lower-cased; a ")
            .Append("new card starts in the \"").Append(WorkflowDefinition.BacklogStageId)
            .Append("\" stage of its own pipeline and is finished in its \"").Append(WorkflowDefinition.DoneStageId)
            .AppendLine("\" stage, which every pipeline ends with.")
            .AppendLine();

        foreach (var workflow in workflows.OrderBy(item => item.Title, StringComparer.Ordinal))
        {
            builder.Append("### ").Append(workflow.CardType)
                .Append(" (workflowId: ").Append(workflow.Id).Append(')');
            if (!string.IsNullOrWhiteSpace(workflow.Description))
            {
                builder.Append(" - ").Append(workflow.Description.Trim());
            }

            builder.AppendLine();
            foreach (var stage in workflow.Stages.OrderBy(item => item.Order))
            {
                builder.Append("- ").Append(stage.Id).Append(" \"").Append(stage.Title).Append('"');
                if (WorkflowDefinition.IsBacklog(stage))
                {
                    builder.Append(" (backlog: a new card starts here)");
                }
                else if (WorkflowDefinition.IsDone(stage))
                {
                    builder.Append(" (done: the pipeline ends here, and only here may a card be finished)");
                }

                if (!string.IsNullOrWhiteSpace(stage.Instruction))
                {
                    builder.Append(": ").Append(stage.Instruction.Trim());
                }

                if (stage.RequiredArtifacts.Count > 0)
                {
                    builder.Append(" Required artifacts: ")
                        .Append(string.Join(", ", stage.RequiredArtifacts.Select(artifact => artifact.Path)))
                        .Append('.');
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The projects this one hands work to, or an empty string when it stands alone.
    /// </summary>
    /// <remarks>
    /// A project with no links contributes nothing rather than an empty section: standing alone is the normal
    /// state, and a heading that says "none" on every project would be read once and skipped forever after.
    /// Asked for by name, the section answers instead - see <see cref="EmptySection"/>.
    /// <para>
    /// Each optional text of a link is printed only when that link carries it, so an entry written before they
    /// existed reads exactly as it did. The reading instruction above the list is the point of the block: an
    /// agent holding a path but no instruction goes on guessing where the neighbour's reference lives.
    /// </para>
    /// </remarks>
    /// <param name="links">The project's linked projects.</param>
    private static string DescribeLinkedProjects(IReadOnlyList<ProjectLink> links)
    {
        if (links.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("## Linked projects");
        builder.AppendLine();
        builder.AppendLine(
            "Work that belongs to one of these projects is filed there with aiko_create_card_in_project, passing "
            + "originProjectId and originCardId so the receiving card remembers where it came from. Read that "
            + "project's own context first - its card types, its stages and its rules are its own - and keep the "
            + "description beside each link in front of you: it says when the neighbour is the right place for a "
            + "piece of work and when it is not.");
        builder.AppendLine();
        builder.AppendLine(
            "Before you reach for something a neighbour owns - a library, a component, a pattern - read the "
            + "reference its entry names rather than inferring it from the packages this project installed, and "
            + "search this project's memory first. aiko_list_links brings these entries back on their own, so "
            + "none of this has to be remembered from the whole document.");
        builder.AppendLine();
        foreach (var link in links.OrderBy(item => item.Handle, StringComparer.Ordinal))
        {
            builder.Append("- ").Append(link.Handle).Append(" - ").Append(link.Description).AppendLine();
            AppendIfPresent(builder, "Reference", link.Reference);
            AppendIfPresent(builder, "Work goes there when", link.WhenToUse);
            AppendIfPresent(builder, "It does not go there when", link.WhenNotToUse);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// One optional text of a link as its own line under the entry, or nothing at all when the link does not
    /// carry it: a label with an empty value would read as a field somebody forgot to fill in.
    /// </summary>
    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("  ").Append(label).Append(": ").Append(value).AppendLine();
        }
    }

    /// <summary>
    /// The scoring and sizing tables under their heading: the block that reads the number a card's criteria
    /// and size step produce.
    /// </summary>
    private static string DescribeScoring(PrioritySettings priority) =>
        $"## How this project scores and sizes a card{Environment.NewLine}{Environment.NewLine}"
        + DescribePriority(priority);

    /// <summary>
    /// How this project's repository treats Aiko's own data and commits, as an agent can act on it.
    /// </summary>
    /// <remarks>
    /// Two policies answer two different questions, and confusing them is easy: the git policy says whether
    /// Aiko's <c>.aiko</c> tree is committed, while the commit policy says whether work in the shared checkout
    /// may be committed. An agent told neither cannot know whether to commit, which is how a commit lands in a
    /// repository that asked to stay local - so both are stated, together with the one fact that always holds:
    /// Aiko never runs git and never creates a commit; it only records what an agent reports.
    /// </remarks>
    /// <param name="gitPolicy">The project's git policy, or null when its manifest could not be read.</param>
    /// <param name="commitPolicy">Whether the shared checkout may be committed.</param>
    /// <param name="pushPolicy">Whether the shared checkout may be pushed from.</param>
    private static string DescribeGit(
        ProjectGitPolicy? gitPolicy,
        ActionPolicy commitPolicy,
        ActionPolicy pushPolicy)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Git and commits");
        builder.AppendLine();
        builder.AppendLine(gitPolicy switch
        {
            ProjectGitPolicy.TrackProjectKnowledge =>
                "Git policy: TrackProjectKnowledge - this project's knowledge is tracked: .aiko is committed "
                + "except the runtime files the .gitignore lists.",
            ProjectGitPolicy.Custom =>
                "Git policy: Custom - the project's .gitignore is managed by hand; Aiko changes nothing there.",
            ProjectGitPolicy.LocalOnly =>
                "Git policy: LocalOnly - Aiko's own data (.aiko) is ignored by git and stays on this machine.",
            _ =>
                "Git policy: unknown - the project manifest could not be read, so treat .aiko as local."
        });
        builder.AppendLine(commitPolicy switch
        {
            ActionPolicy.Allow =>
                "Commit policy: Allow - the shared checkout may be committed: make the commit yourself and "
                + "record it with aiko_report_commit.",
            ActionPolicy.Ask =>
                "Commit policy: Ask - a commit needs the user's approval: propose it with aiko_report_commit "
                + "and wait for the answer.",
            _ =>
                "Commit policy: Deny - do not commit; aiko_report_commit is rejected."
        });
        builder.AppendLine(pushPolicy switch
        {
            ActionPolicy.Allow =>
                "Push policy: Allow - the agent may push from the shared checkout and reports what it pushed.",
            ActionPolicy.Ask =>
                "Push policy: Ask - the agent asks the user before pushing, and waits for the answer.",
            _ =>
                "Push policy: Deny - do not push from the shared checkout."
        });
        builder.Append(
            "The push policy is a rule Aiko states rather than one it enforces: Aiko has no push of its own, so "
            + "nothing pauses an execution on it. Aiko itself never runs git and never creates a commit: it only "
            + "records what you report.");
        return builder.ToString();
    }

    /// <summary>
    /// The scoring rules as an agent can act on them: which criteria to score and what to look at for each,
    /// then the size steps to choose from (ТЗ §10). Both are project content - the agent assigns the values
    /// and the size, so it has to read the tables rather than guess them.
    /// </summary>
    private static string DescribePriority(PrioritySettings priority)
    {
        var builder = new StringBuilder();
        if (priority.Criteria.Count == 0)
        {
            builder.AppendLine("No criteria are configured: a card keeps the own priority it was created with.");
        }
        else
        {
            builder.AppendLine("Score the criteria you have evidence for; the rest stay unscored:");
            foreach (var criterion in priority.Criteria)
            {
                builder.Append("- ").Append(criterion.Id)
                    .Append(" \"").Append(criterion.Title).Append('"')
                    .Append(" range ").Append(criterion.Minimum).Append("..").Append(criterion.Maximum)
                    .Append(" weight ").Append(criterion.Weight);
                var guidance = string.IsNullOrWhiteSpace(criterion.AiInstruction)
                    ? criterion.Description
                    : criterion.AiInstruction;
                if (!string.IsNullOrWhiteSpace(guidance))
                {
                    builder.Append(" - ").Append(guidance);
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine();
        if (priority.Grid.Count == 0)
        {
            builder.AppendLine("No size grid is configured: cards carry no size and no coefficient applies.");
        }
        else
        {
            builder.AppendLine("Assign the card a size step; the coefficient multiplies its score:");
            foreach (var size in priority.Grid)
            {
                builder.Append("- ").Append(size.Id).Append(" (x").Append(size.Coefficient).Append(')');
                if (!string.IsNullOrWhiteSpace(size.Description))
                {
                    builder.Append(" - ").Append(size.Description);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The schemes this project's releases follow: the instruction an agent needs before it conducts one.
    /// </summary>
    /// <remarks>
    /// Every scheme is printed whole rather than pointed at, and that is the point of the section: a scheme is
    /// what the project decided a release looks like, so an agent that reads it works by the project's order
    /// instead of by one it remembers from elsewhere. All of them travel rather than a single scheme "in
    /// force", because which one a release follows is named where that release is asked for - the parameter of
    /// <c>/aiko-release</c>, or the text of the command a release dialog placed. A section carrying only a
    /// chosen scheme could not answer a request that named another one.
    /// </remarks>
    /// <param name="release">The effective release section of the project.</param>
    private static string DescribeRelease(ReleaseSettings release)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## The release schemes of this project");
        builder.AppendLine();
        builder.AppendLine(
            "A release of this project follows one of the schemes below, and which one is named where the "
            + "release is asked for: `/aiko-release <scheme-id>`, or the text of the command that placed it. "
            + "Read the scheme it names and work by its steps rather than from memory; when nothing is named, "
            + "name these schemes and ask which one to follow.");
        builder.AppendLine();
        if (release.IsConfigured)
        {
            builder.Append("- Releases are published in: ").AppendLine(release.Slug);
        }
        else
        {
            builder.AppendLine(
                "- No GitHub repository is configured, so Aiko cannot probe what is published.");
        }

        foreach (var scheme in release.EffectiveSchemes)
        {
            builder.AppendLine();
            builder.Append("### ").Append(scheme.Name).Append(" (").Append(scheme.Id).Append(')').AppendLine();
            builder.AppendLine();
            builder.Append("- What it is for: ").AppendLine(scheme.Description);
            builder.AppendLine();
            builder.AppendLine(scheme.Body);
        }

        builder.AppendLine();
        builder.AppendLine(
            "The history of what this project released is read with aiko_list_releases, and a conducted "
            + "release is recorded with aiko_record_release(version, schemeId, cards).");
        return builder.ToString().TrimEnd();
    }

    [McpServerTool(Name = "aiko_open_ui", Title = "Open Aiko UI")]
    [Description(
        "Returns the canonical local UI URL for the current project and optional card. The caller may open it "
        + "for the user, or hand the link over: it is a page address a browser opens directly.")]
    public async Task<string> OpenUiAsync(
        [Description("Optional card id to select. Pass null for the project board.")]
        string? cardId,
        CancellationToken cancellationToken)
    {
        var context = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HTTP request context is available.");
        // A URL handed to the user is the one the product itself builds: the readable handle - read from the
        // registry, because the MCP route carries a handle only when the agent connected through one - and the
        // page's own route. The shell's root with query parameters was a second address for the same page, and
        // nothing in the PWA ever read those parameters, so the link landed on the dashboard instead.
        var project = await GetProjectAsync(cancellationToken);
        var handle = Uri.EscapeDataString(project.Handle);
        var route = string.IsNullOrWhiteSpace(cardId)
            ? $"/p/{handle}/board"
            : $"/p/{handle}/cards/{Uri.EscapeDataString(cardId)}";
        return $"{context.Request.Scheme}://{context.Request.Host}{route}";
    }
}
