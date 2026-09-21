using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Release;

/// <summary>
/// Supplies a release screen with the facts it shows: the repository releases are published in, the last
/// release found there, and the commit the project's tree is on.
/// </summary>
/// <remarks>
/// Both halves answer instead of throwing - an unconfigured repository, no network and a directory that is
/// not a repository are states a screen draws, not failures of the daemon. The one exception is a project
/// that is not registered at all, because there is nothing to show and the caller asked about something that
/// does not exist.
/// </remarks>
public sealed class ReleaseInfoProvider(
    IProjectCatalog projects,
    IAppSettingsService settings,
    GitHubReleaseProbe probe,
    IGitRefReader refs) : IReleaseInfoProvider
{
    /// <inheritdoc />
    public async ValueTask<ReleaseInfo> ReadAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
        var settingsForRelease = await settings.GetEffectiveReleaseAsync(project.Id, cancellationToken);

        return new ReleaseInfo(
            settingsForRelease,
            settingsForRelease.Slug,
            await probe.ReadAsync(settingsForRelease, cancellationToken),
            refs.Read(project.RootPath));
    }
}
