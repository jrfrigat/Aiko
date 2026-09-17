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
    IProjectCatalog projects,
    IProjectDefinitionStore definitions) : IUnifiedAgentInstaller
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
        var cardTypes = await ReadCardTypesAsync(projectId, cancellationToken);
        var plans = new List<InstallationPlan>();

        foreach (var adapterId in selected)
        {
            plans.Add(await adaptersById[adapterId].PlanProjectInstallAsync(
                project.RootPath,
                projectMcpEndpoint,
                accessToken,
                cardTypes,
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
        var cardTypes = await ReadCardTypesAsync(projectId, cancellationToken);
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
                    cardTypes,
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
    public async ValueTask<IReadOnlyList<AgentInstallationResult>> ReprojectCardTypesAsync(
        string projectId,
        string projectMcpEndpoint,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var cardTypes = await ReadCardTypesAsync(projectId, cancellationToken);
        var results = new List<AgentInstallationResult>();

        foreach (var adapter in adaptersById.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Only the adapters already connected to this project: writing agent files into a project that
            // never installed that agent would be an unasked-for change to someone's working copy.
            if (!await adapter.IsProjectConfiguredAsync(project.RootPath, cancellationToken))
            {
                continue;
            }

            try
            {
                results.Add(await adapter.ApplyProjectInstallAsync(
                    project.RootPath,
                    projectMcpEndpoint,
                    accessToken,
                    cardTypes,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new AgentInstallationResult(
                    adapter.Id,
                    false,
                    [],
                    [$"Adapter re-projection failed: {exception.Message}"]));
            }
        }

        return results;
    }

    /// <summary>
    /// Reads the project's card types for the adapters that generate a command per type.
    /// </summary>
    /// <remarks>
    /// A failure is not fatal: the MCP entry and the stage commands do not depend on the set of types, so a
    /// project whose definitions cannot be read still gets a usable agent configuration.
    /// </remarks>
    private async ValueTask<IReadOnlyList<CardTypeDescriptor>> ReadCardTypesAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = await definitions.ReadAsync(projectId, cancellationToken);
            return definition.Workflows
                .Select(workflow => new CardTypeDescriptor(
                    workflow.Id,
                    workflow.Title,
                    workflow.Description))
                .OrderBy(type => type.Title, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
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
