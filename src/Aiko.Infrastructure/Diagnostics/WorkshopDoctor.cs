using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Execution;
using Aiko.Domain.Workflow;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Diagnostics;

/// <summary>
/// Read-only inspection of a local Aiko installation.
/// </summary>
/// <remarks>
/// Every check answers a question a user actually asks when something is broken: is the database there,
/// is the token there, which port is the daemon on, does the project still exist, and - the one that is
/// invisible otherwise - do the agents' MCP configurations still point at that port. Missing agent files
/// are not reported: an adapter the user never installed is not drift.
/// </remarks>
public sealed class WorkshopDoctor(
    AikoDataPaths paths,
    IProjectCatalog projects,
    IUnifiedAgentInstaller agents,
    IEnumerable<IAgentAdapter> agentAdapters,
    DaemonEndpointConfiguration endpoint,
    AccessTokenStore tokens,
    ICardStore cards,
    IProjectDefinitionStore definitions,
    IExecutionCoordinator executions) : IWorkshopDiagnostics
{
    /// <inheritdoc />
    public async ValueTask<WorkshopDiagnostics> InspectAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();

        findings.Add(new DiagnosticFinding(
            "data",
            File.Exists(paths.DatabasePath) ? DiagnosticSeverity.Ok : DiagnosticSeverity.Warning,
            File.Exists(paths.DatabasePath)
                ? "Database present."
                : "No database yet: run `aiko init <path>` or start the daemon once.",
            paths.DatabasePath));

        findings.Add(new DiagnosticFinding(
            "token",
            File.Exists(paths.AccessTokenPath) ? DiagnosticSeverity.Ok : DiagnosticSeverity.Error,
            File.Exists(paths.AccessTokenPath)
                ? "Access token present."
                : "Access token missing: run `aiko serve` once to create it.",
            paths.AccessTokenPath));

        var settings = await endpoint.TryReadAsync(cancellationToken);
        findings.Add(settings is null
            ? new DiagnosticFinding(
                "endpoint",
                DiagnosticSeverity.Warning,
                "No saved port yet: run `aiko serve` once so the daemon records one.",
                paths.SettingsPath)
            : new DiagnosticFinding(
                "endpoint",
                DiagnosticSeverity.Ok,
                $"Daemon port {settings.Port}.",
                settings.BaseUri.ToString()));

        // One machine-wide fact, reported once however many projects carry a file that names it.
        var credentialVariables = new HashSet<string>(StringComparer.Ordinal);

        var registered = await projects.ListAsync(cancellationToken);
        if (projectId is { Length: > 0 } requested)
        {
            registered = registered
                .Where(project =>
                    StringComparer.Ordinal.Equals(project.Id, requested) ||
                    StringComparer.Ordinal.Equals(project.Slug, requested))
                .ToArray();
            if (registered.Count == 0)
            {
                findings.Add(new DiagnosticFinding(
                    "project",
                    DiagnosticSeverity.Error,
                    $"No registered project with id {requested}."));
            }
        }

        foreach (var project in registered)
        {
            await InspectProjectAsync(project, settings, findings, credentialVariables, cancellationToken);
        }

        if (settings is not null)
        {
            // The user-scope configurations are not tied to a project: they point at the daemon's own
            // /mcp endpoint, so a port change breaks them too.
            foreach (var finding in await FindUserScopeDriftAsync(
                         $"{settings.BaseUri}mcp",
                         cancellationToken))
            {
                findings.Add(finding);
            }
        }

        return new WorkshopDiagnostics(findings);
    }

    private async ValueTask InspectProjectAsync(
        RegisteredProject project,
        DaemonEndpointSettings? settings,
        List<DiagnosticFinding> findings,
        HashSet<string> credentialVariables,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(project.RootPath))
        {
            findings.Add(new DiagnosticFinding(
                "project",
                DiagnosticSeverity.Error,
                $"{project.Name}: the project directory is gone. Unregister it with " +
                $"`aiko project remove {project.Id}`; that does not touch files on disk.",
                project.RootPath));
            return;
        }

        var tree = AikoProjectPaths.DataRoot(project.RootPath);
        if (!Directory.Exists(tree))
        {
            findings.Add(new DiagnosticFinding(
                "project",
                DiagnosticSeverity.Warning,
                $"{project.Name}: no .aiko directory. Run `aiko init {project.RootPath}` to rebuild it.",
                tree));
            return;
        }

        findings.Add(new DiagnosticFinding(
            "project",
            DiagnosticSeverity.Ok,
            $"{project.Name}: registered and present.",
            tree));

        InspectCardLayout(project, findings);
        await InspectCardProgressAsync(project, findings, cancellationToken);

        if (settings is null)
        {
            return;
        }

        var expectedEndpoint = ProjectMcpEndpoint.For(settings.BaseUri.ToString(), project);
        foreach (var finding in await FindAgentConfigDriftAsync(
                     project,
                     expectedEndpoint,
                     credentialVariables,
                     cancellationToken))
        {
            findings.Add(finding);
        }
    }

    /// <summary>
    /// Reports cards still filed the old way, and cards that ended up in both places.
    /// </summary>
    /// <remarks>
    /// A project keeps working with its cards in the old place - reading looks in both - so nothing else
    /// tells the person that its tree is of two shapes at once. This is the check that says it out loud, and
    /// it names the one command that converges the project.
    /// </remarks>
    private static void InspectCardLayout(RegisteredProject project, List<DiagnosticFinding> findings)
    {
        var pending = CardLayoutMigrator.PlannedMoves(project.RootPath);
        if (pending.Count > 0)
        {
            findings.Add(new DiagnosticFinding(
                "card-layout",
                DiagnosticSeverity.Warning,
                $"{project.Name}: {pending.Count} card collection(s) are filed the old way " +
                $"({string.Join(", ", pending)}); run `aiko repair --fix` to move them under .aiko/workflows.",
                AikoProjectPaths.DataRoot(project.RootPath)));
        }

        var duplicated = DuplicatedCards(project.RootPath);
        if (duplicated.Count > 0)
        {
            findings.Add(new DiagnosticFinding(
                "card-layout",
                DiagnosticSeverity.Error,
                $"{project.Name}: {duplicated.Count} card(s) exist in both the old and the new place " +
                $"({string.Join(", ", duplicated)}); the copy under .aiko/workflows is the one read and " +
                "written, so the other is a leftover to remove by hand.",
                AikoProjectPaths.DataRoot(project.RootPath)));
        }
    }

    /// <summary>Card ids that sit in the collection of their type in both roots.</summary>
    private static IReadOnlyList<string> DuplicatedCards(string projectRoot)
    {
        var legacyRoot = AikoProjectPaths.DataRoot(projectRoot);
        var collectionsRoot = AikoProjectPaths.CardCollectionsRoot(projectRoot);
        var duplicates = new List<string>();
        foreach (var collection in FileCardStore.Collections(projectRoot))
        {
            var legacy = Path.Combine(legacyRoot, collection);
            if (!Directory.Exists(legacy))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(collectionsRoot, collection)))
            {
                var cardId = Path.GetFileName(directory);
                if (File.Exists(Path.Combine(directory, "card.json")) &&
                    File.Exists(Path.Combine(legacy, cardId, "card.json")))
                {
                    duplicates.Add(cardId);
                }
            }
        }

        return duplicates;
    }

    /// <summary>
    /// Reports cards whose progress the pipeline cannot account for: cards past the backlog with no execution
    /// at all, and cards that moved on from a stage that was never finished.
    /// </summary>
    /// <remarks>
    /// A card can only leave the backlog by moving into its pipeline, and a stage that ran leaves an execution
    /// behind. A card past the backlog with none of them was pushed along without the pipeline - by hand, or by
    /// an agent that edited files while the card still sat in the backlog - and what that looks like on the
    /// board is a card that appears worked while its runs tab, artifacts and history are empty. The second case
    /// is quieter: the stage was started, so there is a run, but the run never completed and the card was moved
    /// past it anyway, so the card looks further along than the work behind it. Both states are invisible until
    /// someone goes looking, which is why the doctor says them out loud. Read-only, like every other check here.
    /// </remarks>
    private async ValueTask InspectCardProgressAsync(
        RegisteredProject project,
        List<DiagnosticFinding> findings,
        CancellationToken cancellationToken)
    {
        var board = await definitions.ReadAsync(project.Id, cancellationToken);
        var unworked = new List<string>();
        var abandoned = new List<string>();
        foreach (var card in await cards.ListAsync(project.Id, cancellationToken))
        {
            if (StringComparer.Ordinal.Equals(card.StageId, WorkflowDefinition.BacklogStageId))
            {
                continue;
            }

            var runs = await executions.ListAsync(card.Reference, cancellationToken);
            if (runs.Count == 0)
            {
                unworked.Add(card.Reference.CardId);
                continue;
            }

            var leavingStage = PreviousStage(board, card.WorkflowId, card.StageId);
            if (leavingStage is null)
            {
                continue;
            }

            var stageRuns = runs
                .Where(run => StringComparer.Ordinal.Equals(run.StageId, leavingStage.Id))
                .ToArray();
            if (stageRuns.Any(run => run.State == StageExecutionState.Completed))
            {
                continue;
            }

            var state = stageRuns.Length == 0 ? "not started" : Describe(stageRuns[^1].State);
            abandoned.Add($"{card.Reference.CardId} (left '{leavingStage.Id}', which was {state})");
        }

        if (unworked.Count > 0)
        {
            findings.Add(new DiagnosticFinding(
                "card-progress",
                DiagnosticSeverity.Warning,
                $"{project.Name}: {unworked.Count} card(s) left the backlog without a single run: "
                    + $"{Summarize(unworked)}. Nothing was worked through a stage there, so the card has no "
                    + "execution, no artifacts and no history - start the stage with aiko_start_stage before "
                    + "working a card.",
                project.RootPath));
        }

        if (abandoned.Count > 0)
        {
            findings.Add(new DiagnosticFinding(
                "card-progress",
                DiagnosticSeverity.Warning,
                $"{project.Name}: {abandoned.Count} card(s) moved on from a stage that was not finished: "
                    + $"{Summarize(abandoned)}. A card moves on because its stage is completed, not because "
                    + "the card was moved on - continue that stage with aiko_start_stage, or complete it with "
                    + "aiko_complete_stage.",
                project.RootPath));
        }
    }

    /// <summary>The stage a card in <paramref name="stageId"/> came from, or null when there is none to check.</summary>
    /// <remarks>
    /// Walking one step back is enough: a forward move advances a single stage, so the stage just before the
    /// card is the one it left. The backlog is skipped - leaving it is how work begins, and the execution that
    /// follows is what records it.
    /// </remarks>
    private static StageDefinition? PreviousStage(
        ProjectBoardDefinition board,
        string workflowId,
        string stageId)
    {
        var workflow = board.Workflows
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, workflowId));
        if (workflow is null)
        {
            return null;
        }

        var index = -1;
        for (var candidate = 0; candidate < workflow.Stages.Count; candidate++)
        {
            if (StringComparer.Ordinal.Equals(workflow.Stages[candidate].Id, stageId))
            {
                index = candidate;
                break;
            }
        }

        if (index <= 0)
        {
            return null;
        }

        var previous = workflow.Stages[index - 1];
        return WorkflowDefinition.IsBacklog(previous) ? null : previous;
    }

    /// <summary>How a stage run ended, in words a person reads, for the finding above.</summary>
    private static string Describe(StageExecutionState state) => state switch
    {
        StageExecutionState.Running => "still running",
        StageExecutionState.Paused => "paused",
        StageExecutionState.WaitingForUser => "waiting for a user decision",
        StageExecutionState.NeedsAttention => "stopped and needs attention",
        StageExecutionState.Cancelled => "cancelled",
        _ => state.ToString()
    };

    /// <summary>A bounded, comma-separated list for a finding, so a long project still reads.</summary>
    private static string Summarize(IReadOnlyList<string> items)
    {
        var shown = string.Join(", ", items.Take(5));
        return items.Count > 5 ? $"{shown} and {items.Count - 5} more" : shown;
    }

    /// <summary>
    /// Reports agent configuration files that exist but no longer contain the daemon's current MCP
    /// endpoint. This is what a port change leaves behind: valid JSON pointing at a port nothing listens
    /// on, which no other part of the product would notice.
    /// </summary>
    private async ValueTask<IReadOnlyList<DiagnosticFinding>> FindAgentConfigDriftAsync(
        RegisteredProject project,
        string expectedEndpoint,
        HashSet<string> reportedCredentialVariables,
        CancellationToken cancellationToken)
    {
        var discovery = await agents.DiscoverAsync(cancellationToken);
        var installedAdapterIds = discovery
            .Where(adapter => adapter.Installations.Count > 0)
            .Select(adapter => adapter.Id)
            .ToArray();
        if (installedAdapterIds.Length == 0)
        {
            return [];
        }

        // The token the daemon falls back on. What it actually checks is this value unless the daemon was
        // started with the variable set - which is asked per finding below, because it depends on the name.
        var storedToken = await tokens.GetOrCreateAsync(cancellationToken);

        var plan = await agents.PlanAsync(
            project.Id,
            expectedEndpoint,
            storedToken,
            installedAdapterIds,
            cancellationToken);

        var findings = new List<DiagnosticFinding>();
        foreach (var adapterPlan in plan.AdapterPlans)
        {
            foreach (var change in adapterPlan.Changes)
            {
                if (!File.Exists(change.Path))
                {
                    // The user never installed this adapter for the project: not drift, just absent.
                    continue;
                }

                var content = await File.ReadAllTextAsync(change.Path, cancellationToken);
                if (IsStaleProjectEndpoint(content, expectedEndpoint))
                {
                    findings.Add(new DiagnosticFinding(
                        DiagnosticFinding.AgentConfigArea,
                        DiagnosticSeverity.Warning,
                        $"{project.Name}: {adapterPlan.AdapterId} points at a stale endpoint. " +
                        "Run `aiko repair --fix`.",
                        change.Path));
                    continue;
                }

                if (HasEndpoint(content) && !HasCredential(content))
                {
                    // The endpoint is right but nothing authenticates: the daemon answers 401 and the
                    // agent never sees Aiko. This is what an installation predating the token looks like.
                    findings.Add(new DiagnosticFinding(
                        DiagnosticFinding.AgentConfigArea,
                        DiagnosticSeverity.Warning,
                        $"{project.Name}: {adapterPlan.AdapterId} has no access token, so the agent gets " +
                        "401 from the daemon. Run `aiko repair --fix`.",
                        change.Path));
                    continue;
                }

                // A record that names the variable it reads its credential from - Codex cannot store the
                // header itself. A name is not a credential: what counts is the value an agent starting now
                // would read, and whether the daemon would accept it. Reported once per name, however many
                // projects carry such a file, because the variable is one machine-wide fact.
                var variable = AccessTokenVariable(content);
                if (variable is null || !reportedCredentialVariables.Add(variable))
                {
                    continue;
                }

                var available = CredentialVariableValue(variable);
                var daemonToken = Environment.GetEnvironmentVariable(variable) ?? storedToken;
                if (!CredentialVariableIsUsable(available, daemonToken))
                {
                    findings.Add(new DiagnosticFinding(
                        DiagnosticFinding.AgentCredentialArea,
                        DiagnosticSeverity.Warning,
                        string.IsNullOrWhiteSpace(available)
                            ? $"{adapterPlan.AdapterId}: its configuration reads the access token from " +
                              $"{variable}, which is not set, so the agent reaches the daemon without a " +
                              "credential and gets 401. Export it with the value of `aiko token show` in the " +
                              "shell you start the agent from, then restart it."
                            : $"{adapterPlan.AdapterId}: its configuration reads the access token from " +
                              $"{variable}, which holds a different value than the token the daemon checks, " +
                              "so the agent gets 401. Set it to the value of `aiko token show` and restart " +
                              "the agent.",
                        change.Path));
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Reports user-scope agent configuration that points at an old daemon endpoint. The user-scope
    /// files reference the daemon's own <c>/mcp</c>, not a project endpoint, so they are recognised by
    /// the absence of the project path.
    /// </summary>
    private async ValueTask<IReadOnlyList<DiagnosticFinding>> FindUserScopeDriftAsync(
        string expectedEndpoint,
        CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var adapter in agentAdapters)
        {
            var plan = await adapter.PlanUserInstallAsync(cancellationToken);
            foreach (var change in plan.Changes)
            {
                if (!File.Exists(change.Path))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(change.Path, cancellationToken);
                if (!IsStaleUserScopeEndpoint(content, expectedEndpoint))
                {
                    continue;
                }

                findings.Add(new DiagnosticFinding(
                    DiagnosticFinding.AgentConfigArea,
                    DiagnosticSeverity.Warning,
                    $"{adapter.Id} (user scope) points at a stale endpoint. Run `aiko repair --fix`.",
                    change.Path));
            }
        }

        return findings;
    }

    /// <summary>
    /// Whether a file carries an Aiko MCP endpoint at all. Files without one - skills, commands, rules -
    /// are never reported as drift.
    /// </summary>
    public static bool HasEndpoint(string content) =>
        content.Contains(ProjectMcpEndpoint.PathPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether a file carries the credential the daemon requires: an <c>Authorization</c> header, or the
    /// name of the environment variable a client reads it from (Codex).
    /// </summary>
    public static bool HasCredential(string content) =>
        content.Contains("Authorization", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("Bearer ", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("bearer_token_env_var", StringComparison.Ordinal);

    /// <summary>
    /// The name of the environment variable a record reads its credential from, or null when the record
    /// carries the credential itself.
    /// </summary>
    /// <remarks>
    /// Read out of the file rather than taken from a constant: what the doctor answers is whether the
    /// variable <b>this record</b> names is usable, and a constant read here would answer about a different
    /// variable the moment the two disagreed. A spec ties the text the Codex adapter writes to the constant
    /// the adapter writes it from.
    /// </remarks>
    /// <param name="content">Contents of an agent configuration file.</param>
    public static string? AccessTokenVariable(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        const string Marker = "bearer_token_env_var";
        var marker = content.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        // The name is the quoted value that follows the key, whatever whitespace the client's own writer
        // chose around the equals sign.
        var open = content.IndexOf('"', marker + Marker.Length);
        if (open < 0)
        {
            return null;
        }

        var close = content.IndexOf('"', open + 1);
        if (close < 0)
        {
            return null;
        }

        var name = content[(open + 1)..close].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Whether the value a process started now would read is the credential the daemon checks.
    /// </summary>
    /// <remarks>
    /// Not "is it set": a variable holding some other value authenticates nothing, and the daemon prefers
    /// this very variable when it was started with it - so a value that differs from the daemon's is a 401
    /// that looks like nothing at all. The value is never part of a finding.
    /// </remarks>
    /// <param name="value">Value the variable would yield, or null.</param>
    /// <param name="daemonToken">The token the daemon checks.</param>
    public static bool CredentialVariableIsUsable(string? value, string daemonToken) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, daemonToken, StringComparison.Ordinal);

    /// <summary>
    /// The value of a variable as a process started <b>now</b> would see it.
    /// </summary>
    /// <remarks>
    /// The machine's stored value first, because that is what an agent reads when it starts - the daemon
    /// asking this question may have started before the variable was set, and then its own process value
    /// says nothing about the agent's. On a platform without stored variables the process is all there is.
    /// </remarks>
    private static string? CredentialVariableValue(string variable)
    {
        if (OperatingSystem.IsWindows())
        {
            var stored =
                Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                return stored;
            }
        }

        return Environment.GetEnvironmentVariable(variable);
    }

    /// <summary>
    /// Whether a project-scope file embeds a project MCP endpoint that is not the current one. An
    /// adapter also writes skill and command files, which never carry the endpoint: reporting those as
    /// stale would be pure noise.
    /// </summary>
    public static bool IsStaleProjectEndpoint(string content, string expectedEndpoint) =>
        content.Contains(ProjectMcpEndpoint.PathPrefix, StringComparison.Ordinal) &&
        !content.Contains(expectedEndpoint, StringComparison.Ordinal);

    /// <summary>
    /// Whether a user-scope file embeds the daemon-level MCP endpoint and it is not the current one. The
    /// project path in the predicate is what keeps a project-scope file from being counted twice.
    /// </summary>
    public static bool IsStaleUserScopeEndpoint(string content, string expectedEndpoint) =>
        content.Contains("/mcp", StringComparison.Ordinal) &&
        !content.Contains(ProjectMcpEndpoint.PathPrefix, StringComparison.Ordinal) &&
        !content.Contains(expectedEndpoint, StringComparison.Ordinal);
}
