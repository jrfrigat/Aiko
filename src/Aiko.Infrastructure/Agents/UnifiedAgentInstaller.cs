using Aiko.Application.Agents;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Implementation of <see cref="IUnifiedAgentInstaller"/> over a set of
/// <see cref="IAgentAdapter"/> instances: aggregates plans and results, isolates
/// individual adapter failures and reports unknown identifiers separately.
/// </summary>
public sealed class UnifiedAgentInstaller(
    IEnumerable<IAgentAdapter> adapters,
    IProjectCatalog projects) : IUnifiedAgentInstaller
{
    private readonly IReadOnlyDictionary<string, IAgentAdapter> adaptersById = adapters
        .ToDictionary(adapter => adapter.Id, StringComparer.Ordinal);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentAdapterOption>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        var options = new List<AgentAdapterOption>(adaptersById.Count);
        foreach (var adapter in adaptersById.Values.OrderBy(adapter => adapter.DisplayName))
        {
            var installations = await adapter.DetectInstallationsAsync(cancellationToken);
            options.Add(new AgentAdapterOption(
                adapter.Id,
                adapter.DisplayName,
                adapter.Capabilities,
                installations,
                installations.Count > 0,
                await ReadUserScopeAsync(adapter, cancellationToken)));
        }

        return options;
    }

    /// <summary>
    /// Counts how many of the adapter's user-scope files are on disk. This is what separates "the agent is
    /// installed" (the executable is on PATH) from "Aiko is connected to it" (the global skills and
    /// commands are there) in the dashboard.
    /// </summary>
    private static async ValueTask<AgentUserScope> ReadUserScopeAsync(
        IAgentAdapter adapter,
        CancellationToken cancellationToken)
    {
        var plan = await adapter.PlanUserInstallAsync(cancellationToken);
        var configured = plan.Changes.Count(change => File.Exists(change.Path));
        return new AgentUserScope(configured, plan.Changes.Count);
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult?> ApplyUserInstallAsync(
        string adapterId,
        CancellationToken cancellationToken)
    {
        if (!adaptersById.TryGetValue(adapterId, out var adapter))
        {
            return null;
        }

        return await adapter.ApplyUserInstallAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult?> UninstallUserAsync(
        string adapterId,
        CancellationToken cancellationToken)
    {
        if (!adaptersById.TryGetValue(adapterId, out var adapter))
        {
            return null;
        }

        return await adapter.UninstallUserAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<UnifiedInstallationPlan> PlanAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken)
    {
        var (project, selected, unknown) = await ResolveSelectionAsync(
            projectId, selectedAdapterIds, cancellationToken);
        var plans = new List<InstallationPlan>();

        foreach (var adapterId in selected)
        {
            plans.Add(await adaptersById[adapterId].PlanProjectInstallAsync(
                project.RootPath,
                projectMcpEndpoint,
                accessToken,
                cancellationToken));
        }

        return new UnifiedInstallationPlan(
            project.Id,
            projectMcpEndpoint,
            plans,
            unknown);
    }

    /// <inheritdoc />
    public async ValueTask<UnifiedInstallationResult> ApplyAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken)
    {
        var (project, selected, unknown) = await ResolveSelectionAsync(
            projectId, selectedAdapterIds, cancellationToken);
        var results = new List<AgentInstallationResult>();

        foreach (var adapterId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await adaptersById[adapterId].ApplyProjectInstallAsync(
                    project.RootPath,
                    projectMcpEndpoint,
                    accessToken,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new AgentInstallationResult(
                    adapterId,
                    false,
                    [],
                    [$"Adapter installation failed: {exception.Message}"]));
            }
        }

        return new UnifiedInstallationResult(
            project.Id,
            projectMcpEndpoint,
            results,
            unknown);
    }

    /// <inheritdoc />
    public async ValueTask<UnifiedUninstallationPlan> PlanUninstallAsync(
        string projectId,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken)
    {
        var (project, selected, unknown) = await ResolveSelectionAsync(
            projectId, selectedAdapterIds, cancellationToken);
        var plans = new List<InstallationPlan>();

        foreach (var adapterId in selected)
        {
            plans.Add(await adaptersById[adapterId].PlanProjectUninstallAsync(
                project.RootPath,
                cancellationToken));
        }

        return new UnifiedUninstallationPlan(project.Id, plans, unknown);
    }

    /// <inheritdoc />
    public async ValueTask<UnifiedUninstallationResult> UninstallAsync(
        string projectId,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken)
    {
        var (project, selected, unknown) = await ResolveSelectionAsync(
            projectId, selectedAdapterIds, cancellationToken);
        var results = new List<AgentInstallationResult>();

        foreach (var adapterId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await adaptersById[adapterId].UninstallProjectAsync(
                    project.RootPath,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new AgentInstallationResult(
                    adapterId,
                    false,
                    [],
                    [$"Adapter uninstallation failed: {exception.Message}"]));
            }
        }

        return new UnifiedUninstallationResult(project.Id, results, unknown);
    }

    /// <summary>
    /// Resolves the project and normalizes the adapter selection shared by every
    /// operation: resolves the project, drops blank and duplicate identifiers and
    /// separates the identifiers unknown to this installer.
    /// </summary>
    private async ValueTask<(RegisteredProject Project, string[] Selected, string[] Unknown)> ResolveSelectionAsync(
        string projectId,
        IReadOnlyList<string> selectedAdapterIds,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var selected = selectedAdapterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = selected
            .Where(id => !adaptersById.ContainsKey(id))
            .ToArray();
        return (project,
            selected.Where(adaptersById.ContainsKey).ToArray(),
            unknown);
    }
}
