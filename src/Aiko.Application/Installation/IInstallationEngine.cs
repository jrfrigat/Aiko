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

    /// <summary>
    /// Puts a release that is already beside the installation in place: everything an install and an update
    /// do once the release has been fetched, verified and unpacked.
    /// </summary>
    /// <remarks>
    /// Published because the half of the run before it is not always reachable. On a machine with neither
    /// Aiko nor .NET the release is fetched and unpacked by the bootstrap script, and the freshly unpacked
    /// CLI finishes the job here - one sequence with two callers, rather than a second spelling of
    /// "validate, stop, replace, record, repair, report" beside the script.
    /// </remarks>
    /// <param name="request">What the run was asked for: the directory, the PATH choice, the agents.</param>
    /// <param name="staged">A release already unpacked beside the installation.</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    ValueTask<InstallationReport> FinishAsync(
        InstallationRequest request,
        StagedRelease staged,
        CancellationToken cancellationToken);
}
