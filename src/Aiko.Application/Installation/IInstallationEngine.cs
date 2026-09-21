namespace Aiko.Application.Installation;

/// <summary>
/// The installation engine: one path that both installs and updates Aiko.
/// </summary>
/// <remarks>
/// Two verbs, one engine. <c>aiko update</c>, the <c>/aiko-update</c> skill and the installer executable all
/// call these methods, so the difference between installing and updating is what the engine finds on disk -
/// not a second implementation that would have to be kept in step with this one.
/// </remarks>
public interface IInstallationEngine
{
    /// <summary>Installs the resolved release into the requested directory.</summary>
    /// <param name="request">What to install, and where.</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    ValueTask<InstallationReport> InstallAsync(InstallationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Brings an existing installation up to the resolved release, leaving the data and the projects alone.
    /// </summary>
    /// <param name="request">What to update, and where.</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    ValueTask<InstallationReport> UpdateAsync(InstallationRequest request, CancellationToken cancellationToken);
}
