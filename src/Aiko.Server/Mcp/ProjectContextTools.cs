using System.ComponentModel;
using System.Text;
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
        + "pipeline, how its cards are scored and sized, and durable-memory guidance.")]
    public async Task<string> GetProjectContextAsync(CancellationToken cancellationToken)
    {
        var project = await GetProjectAsync(cancellationToken);
        var priority = await settings.GetEffectivePriorityAsync(project.Id, cancellationToken);
        var execution = await settings.GetEffectiveExecutionAsync(project.Id, cancellationToken);
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

        return $"""
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
            --all, which walks the pipeline and still stops on a question to the user, a failure or a forbidden
            action.

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

            {DescribeGit(gitPolicy, execution.SharedCheckoutCommitPolicy, execution.SharedCheckoutPushPolicy)}

            {DescribeLinkedProjects(linked)}

            ## How this project scores and sizes a card

            {DescribePriority(priority)}

            {DescribeCardTypes(workflows)}
            {initialization}
            """;
    }

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
            .AppendLine("\" stage of its own pipeline.");
        builder.AppendLine();

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
        foreach (var link in links.OrderBy(item => item.Handle, StringComparer.Ordinal))
        {
            builder.Append("- ").Append(link.Handle).Append(" - ").Append(link.Description).AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

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

    [McpServerTool(Name = "aiko_open_ui", Title = "Open Aiko UI")]
    [Description(
        "Returns the local UI URL for the current project and optional card. The caller may open it for the user.")]
    public string OpenUi(
        [Description("Optional card id to select. Pass null for the project board.")]
        string? cardId)
    {
        var context = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HTTP request context is available.");
        var query = $"project={Uri.EscapeDataString(GetProjectId())}";
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            query += $"&card={Uri.EscapeDataString(cardId)}";
        }

        return $"{context.Request.Scheme}://{context.Request.Host}/?{query}";
    }
}
