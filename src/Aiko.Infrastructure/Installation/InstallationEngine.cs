using Aiko.Application.Contracts;
using Aiko.Application.Installation;

namespace Aiko.Infrastructure.Installation;

/// <summary>
/// The one path that installs and updates Aiko: resolve, stage, stop, replace, start, repair, report.
/// </summary>
/// <remarks>
/// This type is thin on purpose, and that is its design: every part that could be a second implementation
/// already exists beside it - <see cref="ReleaseStaging"/> downloads and verifies,
/// <see cref="InstallationReplacement"/> swaps the entries and records the version,
/// <see cref="IReleaseSource"/> says where a release comes from, <see cref="IDaemonLifecycle"/> deals with
/// the process, and <see cref="IInstallationRepair"/> with what the new build expects of the projects and
/// the agents. What is left here is the order and the decisions between them.
/// <para>
/// The order is the guarantee. Nothing is written until the release has been downloaded, verified against
/// its published checksum, unpacked and found complete <b>beside</b> the installation - which is why staging
/// comes before the daemon is stopped rather than after: a refusal at that point must leave the running
/// daemon running, and only the replacement itself needs the files free. Installing and updating are the same
/// sequence; what differs is what the run finds on disk.
/// </para>
/// <para>
/// A decision the engine made is reported, a fault is not: <see cref="InstallationRefusedException"/> becomes
/// an <see cref="InstallationOutcome.Refused"/> report, and anything else escapes. Reporting a crash as a
/// refusal would tell the user their installation is fine when it was never touched.
/// </para>
/// </remarks>
public sealed class InstallationEngine(
    IReleaseSource source,
    IDaemonLifecycle daemon,
    IInstallationRepair repair,
    IWorkshopDiagnostics diagnostics) : IInstallationEngine
{
    /// <inheritdoc />
    public ValueTask<InstallationReport> InstallAsync(
        InstallationRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<InstallationReport> UpdateAsync(
        InstallationRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(request, cancellationToken);

    /// <summary>
    /// The one sequence both verbs run: install and update differ by what is on disk, not by what is done.
    /// </summary>
    private async ValueTask<InstallationReport> RunAsync(
        InstallationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstallDirectory);

        var installDirectory = Path.GetFullPath(request.InstallDirectory);
        var steps = new List<InstallationStep>();

        // The tag is resolved first in every verb, including --check: it is the half of "installed X,
        // available Y" that cannot be read off the disk.
        var tag = await source.ResolveTagAsync(request.Tag, cancellationToken);
        var version = ReleaseNaming.VersionFromTag(tag);
        steps.Add(new InstallationStep("resolve", true, $"Resolved {tag}."));

        var installed = InstalledVersionFile.Read(installDirectory);
        var available = AvailableVersion(tag, version, installDirectory);

        if (request.Check)
        {
            return new InstallationReport(
                InstallationOutcome.Reported,
                installed,
                available,
                steps,
                installed is null
                    ? $"Nothing is installed in {installDirectory}; {tag} is available."
                    : $"Installed {installed.Tag}, available {tag}.");
        }

        if (installed is not null && !request.Force)
        {
            if (string.Equals(installed.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return UpToDate(installed, available, steps, tag);
            }

            if (IsOlder(version, installed.Version))
            {
                var refusal =
                    $"The installed {installed.Tag} is newer than {tag}. Nothing was written; " +
                    "pass --force to put the older release in place.";
                steps.Add(new InstallationStep("replace", false, refusal));
                return new InstallationReport(InstallationOutcome.Refused, installed, available, steps, refusal);
            }
        }

        // Before anything is downloaded, unpacked or stopped: a directory holding someone else's files is not
        // the installer's to empty, and saying so costs nothing yet.
        if (InstallationReplacement.DescribeForeignContent(installDirectory) is { } foreign)
        {
            steps.Add(new InstallationStep("replace", false, foreign));
            return new InstallationReport(InstallationOutcome.Refused, installed, available, steps, foreign);
        }

        StagedRelease staged;
        try
        {
            staged = await new ReleaseStaging(source).PrepareAsync(tag, installDirectory, cancellationToken);
        }
        catch (InstallationRefusedException refusal)
        {
            steps.Add(new InstallationStep("stage", false, refusal.Message));
            return new InstallationReport(InstallationOutcome.Refused, installed, available, steps, refusal.Message);
        }
        steps.Add(new InstallationStep(
            "stage",
            true,
            $"{staged.AssetName} verified against its published checksum ({staged.Sha256[..12]}) " +
            "and unpacked beside the installation."));

        // A daemon holds the binaries, so it has to be gone before they are renamed. If it will not go, the
        // run refuses: replacing files under a live process is the one thing this step exists to prevent.
        var wasRunning = await daemon.IsRunningAsync(cancellationToken);
        if (!wasRunning)
        {
            steps.Add(new InstallationStep("daemon", true, "No daemon was running."));
        }
        else
        {
            try
            {
                await daemon.StopAsync(cancellationToken);
                steps.Add(new InstallationStep(
                    "daemon",
                    true,
                    "Stopped the running daemon before replacing the files."));
            }
            catch (InstallationRefusedException refusal)
            {
                Discard(staged.Directory);
                steps.Add(new InstallationStep("daemon", false, refusal.Message));
                return new InstallationReport(
                    InstallationOutcome.Refused,
                    installed,
                    available,
                    steps,
                    refusal.Message);
            }
        }

        ReplacementResult replaced;
        try
        {
            replaced = new InstallationReplacement().Apply(staged, installDirectory, request.UpdatePath);
        }
        catch
        {
            // The replacement undoes itself, and what it cannot undo is a daemon left down: bring it back
            // before the fault travels, so a failed update costs a restart and nothing else.
            if (wasRunning)
            {
                await daemon.StartAsync(installDirectory, CancellationToken.None);
            }

            throw;
        }

        // The staging directory has been emptied by the swap, and an empty one left beside the installation
        // is what a later run would have to decide about. The release itself now lives in the installation.
        Discard(staged.Directory);

        steps.Add(new InstallationStep(
            "replace",
            true,
            $"{replaced.EntriesReplaced} entries replaced" +
            (replaced.RecoveredPrevious
                ? "; an interrupted earlier replacement was repaired first"
                : string.Empty) +
            "."));
        steps.Add(new InstallationStep("record", true, $"install.json records {replaced.Version.Tag}."));
        steps.Add(new InstallationStep(
            "path",
            true,
            request.UpdatePath
                ? $"The user PATH names {installDirectory}."
                : "The user PATH was left as it is (--no-path)."));
        var daemonCameBack = true;
        if (wasRunning)
        {
            daemonCameBack = await daemon.StartAsync(installDirectory, cancellationToken);
            steps.Add(new InstallationStep(
                "daemon",
                daemonCameBack,
                daemonCameBack
                    ? "Restarted the daemon on the new binaries."
                    : "The daemon did not come back up; start it with `aiko serve`."));
        }

        steps.AddRange(await repair.RepairAsync(
            new InstallationAgentSelection(request.Agents, request.NoAgents),
            cancellationToken));

        steps.Add(await DoctorStepAsync(cancellationToken));

        var outcome = installed is null ? InstallationOutcome.Installed : InstallationOutcome.Updated;
        var summary = installed is null
            ? $"Installed {tag} into {installDirectory}."
            : $"Updated {installed.Tag} to {tag} in {installDirectory}.";
        if (!daemonCameBack)
        {
            summary += " The daemon did not come back up; start it with `aiko serve`.";
        }

        return new InstallationReport(outcome, installed, available, steps, summary);
    }

    /// <summary>
    /// The doctor report the run ends with, as a step: an update that leaves a stale agent configuration
    /// behind has not finished its job, and this is where that becomes visible.
    /// </summary>
    private async ValueTask<InstallationStep> DoctorStepAsync(CancellationToken cancellationToken)
    {
        var report = await diagnostics.InspectAsync(projectId: null, cancellationToken);
        var problems = report.Findings
            .Where(finding => finding.Severity != DiagnosticSeverity.Ok)
            .ToArray();

        // The step ran either way; what it found is the detail, not the outcome. A finding is the doctor's
        // answer, and calling the step a failure would make "the report ran" and "Aiko is broken" one thing.
        return new InstallationStep(
            "doctor",
            true,
            problems.Length == 0
                ? "Everything looks healthy."
                : $"{problems.Length} finding(s) need attention: " +
                    string.Join("; ", problems.Select(finding => finding.Summary)));
    }

    /// <summary>
    /// The resolved release as a version record.
    /// </summary>
    /// <remarks>
    /// <see cref="InstalledVersion"/> carries when a version was put in place, and a release that is only
    /// available has no such moment - the field is left at its default rather than filled with the time of
    /// the lookup, which a later caller could mistake for an installation. What <c>--check</c> and
    /// <c>--version</c> read is the tag and the version.
    /// </remarks>
    private static InstalledVersion AvailableVersion(string tag, string version, string installDirectory) =>
        new(tag, version, InstalledVersion.LatestChannel, default, installDirectory);

    private static InstallationReport UpToDate(
        InstalledVersion installed,
        InstalledVersion available,
        IReadOnlyList<InstallationStep> steps,
        string tag)
    {
        var stepsWithOutcome = steps.ToList();
        stepsWithOutcome.Add(new InstallationStep("replace", true, $"Already at {tag}; nothing was replaced."));
        return new InstallationReport(
            InstallationOutcome.UpToDate,
            installed,
            available,
            stepsWithOutcome,
            $"Already at {tag}; nothing was written.");
    }

    /// <summary>
    /// Whether the release about to be installed is older than the one in place, as far as the tags say.
    /// </summary>
    /// <remarks>
    /// Only tags that parse as a version are ordered. A tag that does not - a date, a branch name - is not
    /// guessed at: two versions that cannot be compared are then treated as different, and the run replaces
    /// them, which is what a caller who named that tag asked for.
    /// </remarks>
    private static bool IsOlder(string candidate, string installed) =>
        Version.TryParse(candidate, out var candidateVersion) &&
        Version.TryParse(installed, out var installedVersion) &&
        candidateVersion < installedVersion;

    private static void Discard(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What is left is a staging directory beside the installation, and the next run discards it.
            // Failing here would replace the reason the run stopped with a complaint about tidying up.
        }
    }
}

