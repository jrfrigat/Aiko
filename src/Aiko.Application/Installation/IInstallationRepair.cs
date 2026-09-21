namespace Aiko.Application.Installation;

/// <summary>
/// Which agents the run should leave connected once the new binaries are in place.
/// </summary>
/// <remarks>
/// The two instructions an install carries are not one with a default: "connect none of them"
/// (<c>--no-agents</c>) and "do not touch the agents" are different, and a selection that could not tell
/// them apart would turn the second into the first on every install that said nothing.
/// </remarks>
/// <param name="Agents">Adapter identifiers to connect, or null when the run named none.</param>
/// <param name="NoAgents">Whether the run asked for nobody to be connected.</param>
public sealed record InstallationAgentSelection(IReadOnlyList<string>? Agents, bool NoAgents)
{
    /// <summary>Nothing was said about the agents, so what is already configured is re-applied as it stands.</summary>
    public static InstallationAgentSelection Configured { get; } = new(null, false);

    /// <summary>
    /// The adapters to re-apply when the run named none: the ones this machine actually has, because
    /// re-applying an integration to an agent that is not installed writes files nobody reads.
    /// </summary>
    /// <param name="installedAdapterIds">Adapters detected on this machine.</param>
    public IReadOnlyList<string> Targets(IReadOnlyList<string> installedAdapterIds)
    {
        ArgumentNullException.ThrowIfNull(installedAdapterIds);
        if (NoAgents)
        {
            return [];
        }

        return Agents is { Count: > 0 } named
            ? named.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()
            : installedAdapterIds;
    }
}

/// <summary>
/// The repair an installation run performs after it replaced the binaries.
/// </summary>
/// <remarks>
/// A seam rather than a call to the reindexer and the agent installer directly, because the engine's subject
/// is the files it replaced: what a repair consists of - which projects are reindexed, which agent files are
/// rewritten - is not something it should have to know. The same repair is what <c>aiko repair --fix</c>
/// performs, and a second spelling of it is how the two come to disagree.
/// </remarks>
public interface IInstallationRepair
{
    /// <summary>
    /// Brings the projects and the agent integrations back to the shape this build expects.
    /// </summary>
    /// <param name="selection">Which agents to leave connected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One step per thing that was touched, in the order it happened.</returns>
    ValueTask<IReadOnlyList<InstallationStep>> RepairAsync(
        InstallationAgentSelection selection,
        CancellationToken cancellationToken);
}
