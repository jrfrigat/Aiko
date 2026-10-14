namespace Aiko.Application.Installation;

/// <summary>
/// The installation engine: one path that both installs and updates Aiko.
/// </summary>
/// <remarks>
/// Installing and updating are two names of one path: the difference between them is what the engine finds on
/// disk, not a second implementation that would have to be kept in step with this one.
/// </remarks>
public interface IInstallationEngine
{
    /// <summary>Installs the resolved release into the requested directory.</summary>
    ValueTask<InstallationReport> InstallAsync(InstallationRequest request, CancellationToken cancellationToken);

    /// <summary>Brings an existing installation up to the resolved release, leaving the data and the projects alone.</summary>
    ValueTask<InstallationReport> UpdateAsync(InstallationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a release that is already beside the installation in place: everything an install and an update do
    /// once the release has been fetched, verified and unpacked.
    /// </summary>
    /// <remarks>
    /// Published because the half before it is not always reachable: on a machine with neither Aiko nor .NET the
    /// bootstrap script fetches and unpacks, and the freshly unpacked CLI finishes the job here - one sequence
    /// with two callers, rather than a second spelling of "validate, stop, replace, record, repair, report".
    /// </remarks>
    ValueTask<InstallationReport> FinishAsync(
        InstallationRequest request,
        StagedRelease staged,
        CancellationToken cancellationToken);
}
