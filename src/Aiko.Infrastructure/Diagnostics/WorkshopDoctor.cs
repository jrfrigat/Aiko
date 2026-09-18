using Aiko.Application.Agents;
using Aiko.Application.Contracts;
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
    AccessTokenStore tokens) : IWorkshopDiagnostics
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
            await InspectProjectAsync(project, settings, findings, cancellationToken);
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

        if (settings is null)
        {
            return;
        }

        var expectedEndpoint = ProjectMcpEndpoint.For(settings.BaseUri.ToString(), project);
        foreach (var finding in await FindAgentConfigDriftAsync(
                     project,
                     expectedEndpoint,
                     cancellationToken))
        {
            findings.Add(finding);
        }
    }

    /// <summary>
    /// Reports agent configuration files that exist but no longer contain the daemon's current MCP
    /// endpoint. This is what a port change leaves behind: valid JSON pointing at a port nothing listens
    /// on, which no other part of the product would notice.
    /// </summary>
    private async ValueTask<IReadOnlyList<DiagnosticFinding>> FindAgentConfigDriftAsync(
        RegisteredProject project,
        string expectedEndpoint,
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

        var plan = await agents.PlanAsync(
            project.Id,
            expectedEndpoint,
            await tokens.GetOrCreateAsync(cancellationToken),
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
                        "agent-config",
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
                        "agent-config",
                        DiagnosticSeverity.Warning,
                        $"{project.Name}: {adapterPlan.AdapterId} has no access token, so the agent gets " +
                        "401 from the daemon. Run `aiko repair --fix`.",
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
                    "agent-config",
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
