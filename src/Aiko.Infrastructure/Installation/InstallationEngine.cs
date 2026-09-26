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

        // The two rules that can decide before anything is fetched, so that a release which would be refused
        // is not downloaded either. FinishAsync applies the same two to a caller that arrives with a staged
        // release: one rule each, evaluated where it saves the most work.
        if (installed is not null && !request.Force)
        {
            if (string.Equals(installed.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return UpToDate(installed, available, steps, tag);
            }

            if (OlderRefusal(installed, tag, version) is { } refusal)
            {
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

        // From here the rest of a run is one method: it is what a caller that brings its own staged release
        // also does, and a second spelling of it would be the drift this engine exists to stop.
        var finished = await FinishAsync(request, staged, cancellationToken);
        return finished with { Steps = [.. steps, .. finished.Steps] };
    }

    /// <summary>
    /// Puts a release that is already beside the installation in place: the half of a run after the download.
    /// </summary>
    /// <remarks>
    /// Reached two ways: <see cref="RunAsync"/> continues into it, and a caller that fetched and unpacked the
    /// release itself enters here directly - which is what a machine with neither Aiko nor .NET does, because
    /// there the engine cannot be the one that downloads. The order is the guarantee in both cases: nothing is
    /// stopped or replaced until the release in front of it has been found whole.
    /// </remarks>
    /// <param name="request">What the run was asked for: the directory, the PATH choice, the agents.</param>
    /// <param name="staged">A release already unpacked beside the installation.</param>
    /// <param name="cancellationToken">Cancellation token for the run.</param>
    public async ValueTask<InstallationReport> FinishAsync(
        InstallationRequest request,
        StagedRelease staged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstallDirectory);
        ArgumentNullException.ThrowIfNull(staged);

        var installDirectory = Path.GetFullPath(request.InstallDirectory);
        var steps = new List<InstallationStep>();
        var installed = InstalledVersionFile.Read(installDirectory);
        var available = AvailableVersion(staged.Tag, staged.Version, installDirectory);

        InstallationReport Refuse(string refusal)
        {
            steps.Add(new InstallationStep("replace", false, refusal));
            return new InstallationReport(InstallationOutcome.Refused, installed, available, steps, refusal);
        }

        // What the caller staged has to be a release: the workflow refuses to publish one without these
        // entries, so a directory missing one is an unpacking that went wrong - and replacing a working
        // installation with half of a release is the one outcome nothing later can undo.
        if (MissingRequiredEntry(staged.Directory) is { } missing)
        {
            steps.Add(new InstallationStep("layout", false, $"The staged release has no {missing}."));
            return Refuse(
                $"The staged release in {staged.Directory} does not carry {missing}, so it is not a release "
                + "and nothing was replaced.");
        }

        steps.Add(new InstallationStep(
            "layout",
            true,
            "The staged release carries every entry a release promises."));

        if (installed is not null && !request.Force)
        {
            if (string.Equals(installed.Tag, staged.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return UpToDate(installed, available, steps, staged.Tag);
            }

            if (OlderRefusal(installed, staged.Tag, staged.Version) is { } refusal)
            {
                return Refuse(refusal);
            }
        }

        // Before anything is stopped or replaced: a directory holding someone else's files is not the
        // installer's to empty.
        if (InstallationReplacement.DescribeForeignContent(installDirectory) is { } foreign)
        {
            return Refuse(foreign);
        }

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

        // The swap moves the release out of the directory it was staged in, so what is left is often nothing at
        // all - and an empty one beside the installation is what a later run would have to decide about. When
        // the release being installed is the one this process runs from, the entries were copied instead and
        // the directory still holds the whole of it, executable included, because a running file cannot be
        // deleted. That directory is the caller's, so the caller removes it once this process has ended - and
        // the report says it is still there rather than leaving it to be discovered.
        var stagingKept = !Discard(staged.Directory);

        steps.Add(new InstallationStep(
            "replace",
            true,
            $"{replaced.EntriesReplaced} entries replaced" +
            (replaced.RecoveredPrevious
                ? "; an interrupted earlier replacement was repaired first"
                : string.Empty) +
            (replaced.PreviousKept
                ? "; the replaced version is still beside the installation and the next run removes it"
                : string.Empty) +
            (stagingKept
                ? $"; the staged release is still in {staged.Directory} and is the caller's to remove"
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
            ? $"Installed {staged.Tag} into {installDirectory}."
            : $"Updated {installed.Tag} to {staged.Tag} in {installDirectory}.";
        if (!daemonCameBack)
        {
            summary += " The daemon did not come back up; start it with `aiko serve`.";
        }

        return new InstallationReport(outcome, installed, available, steps, summary);
    }

    /// <summary>
    /// The first entry a release promises that a directory does not carry, or null when it carries them all.
    /// </summary>
    private static string? MissingRequiredEntry(string directory) =>
        ReleaseLayout.RequiredEntries.FirstOrDefault(entry => !File.Exists(Path.Combine(directory, entry)));

    /// <summary>
    /// Why an older release may not replace the one in place, or null when nothing in place is newer.
    /// </summary>
    /// <remarks>
    /// One rule, asked at both places that need it: <see cref="RunAsync"/> asks before it downloads, so a
    /// refusal costs no bandwidth, and <see cref="FinishAsync"/> asks again for a caller that arrives with a
    /// staged release already in hand.
    /// </remarks>
    private static string? OlderRefusal(InstalledVersion installed, string tag, string version) =>
        IsOlder(version, installed.Version)
            ? $"The installed {installed.Tag} is newer than {tag}. Nothing was written; pass --force to put "
                + "the older release in place."
            : null;

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

    /// <summary>
    /// Removes a directory if it can, and says whether it is gone.
    /// </summary>
    /// <remarks>
    /// What stands in the way is a file in use. For a staging directory that is the release's own executable
    /// while the run that installed it is working from it; for a refused run it is whatever the person still
    /// has open. Neither is a reason to replace the outcome of the run with a complaint about tidying up, so
    /// the answer is returned and the caller says what it means.
    /// </remarks>
    private static bool Discard(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return !Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

