using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Application.Installation;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// The repair an installation run performs once the new binaries are in place, assembled from the services
/// that already do each part of it.
/// </summary>
/// <remarks>
/// Nothing here is new behaviour: the reindex is <see cref="IProjectReindexer"/>, the old card layout is
/// moved by <see cref="CardLayoutMigrator"/>, and the agent integration is rewritten by
/// <see cref="IUnifiedAgentInstaller"/> - the same three <c>aiko repair --fix</c> uses. What is new is only
/// that an update runs them too, because an update is exactly the moment the projects on this machine stop
/// matching the build that now reads them, and an agent configuration that still names the old build would
/// otherwise go stale without anyone being told.
/// </remarks>
public sealed class InstallationRepair(
    IProjectCatalog projects,
    IProjectReindexer reindexer,
    IUnifiedAgentInstaller agentInstaller) : IInstallationRepair
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InstallationStep>> RepairAsync(
        InstallationAgentSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var steps = new List<InstallationStep>();
        foreach (var project in await projects.ListAsync(cancellationToken))
        {
            if (!Directory.Exists(AikoProjectPaths.DataRoot(project.RootPath)))
            {
                steps.Add(new InstallationStep(
                    "reindex",
                    true,
                    $"{project.Name}: skipped, no .aiko directory to rebuild from."));
                continue;
            }

            // Idempotent, and that is the point: a second update finds nothing left to move. A project whose
            // cards still sit in the old layout is one the new build would read as empty.
            var migration = CardLayoutMigrator.Migrate(project.RootPath);
            if (migration.Moved.Count > 0)
            {
                steps.Add(new InstallationStep(
                    "layout",
                    true,
                    $"{project.Name}: filed {migration.Moved.Count} card collection(s) under .aiko/workflows " +
                    $"({string.Join(", ", migration.Moved)})."));
            }

            var reindexed = await reindexer.ReindexAsync(project.Id, cancellationToken);
            steps.Add(new InstallationStep(
                "reindex",
                true,
                $"{project.Name}: {reindexed.Cards} cards, {reindexed.Relations} relations, " +
                $"{reindexed.MemoryDocuments} memory documents."));
        }

        steps.AddRange(await ReapplyAgentsAsync(selection, cancellationToken));
        return steps;
    }

    /// <summary>
    /// Rewrites the user-scope integration of the agents this run should leave connected.
    /// </summary>
    /// <remarks>
    /// The files carry the daemon's endpoint and version, so they are stale by definition after a replacement
    /// - which is what <c>aiko doctor</c> would report and what this step exists to prevent. Only agents that
    /// are actually installed are re-applied when the run named none: writing an integration for an agent
    /// nobody has is how a machine ends up with files for a program that is not there.
    /// </remarks>
    private async ValueTask<IReadOnlyList<InstallationStep>> ReapplyAgentsAsync(
        InstallationAgentSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.NoAgents)
        {
            return [new InstallationStep("agents", true, "No agent was touched (--no-agents).")];
        }

        var discovered = await agentInstaller.DiscoverAsync(cancellationToken);
        var installed = discovered
            .Where(option => option.Installations.Count > 0)
            .Select(option => option.Id)
            .ToArray();
        var targets = selection.Targets(installed);
        if (targets.Count == 0)
        {
            return [new InstallationStep("agents", true, "No installed agent was found to connect.")];
        }

        var known = discovered.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
        var steps = new List<InstallationStep>();
        foreach (var adapterId in targets)
        {
            if (!known.Contains(adapterId))
            {
                steps.Add(new InstallationStep(
                    $"agents:{adapterId}",
                    false,
                    "No adapter is known by that id, so nothing was written for it."));
                continue;
            }

            var applied = await agentInstaller.ApplyUserInstallAsync(adapterId, cancellationToken);
            if (applied is null)
            {
                steps.Add(new InstallationStep($"agents:{adapterId}", false, "No adapter is known by that id."));
                continue;
            }

            var changed = applied.Files.Count(file => file.Status is not InstallationFileStatus.Unchanged);
            steps.Add(new InstallationStep(
                $"agents:{adapterId}",
                applied.Succeeded,
                applied.Succeeded
                    ? $"{changed} file(s) rewritten."
                    : string.Join("; ", applied.Warnings)));
        }

        return steps;
    }
}
