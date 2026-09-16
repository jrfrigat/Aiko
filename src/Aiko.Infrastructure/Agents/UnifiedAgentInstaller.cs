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
                installations.Count > 0));
        }

        return options;
    }

    /// <inheritdoc />
    public async ValueTask<UnifiedInstallationPlan> PlanAsync(
        string projectId,
        string projectMcpEndpoint,
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
