using System.Security.Cryptography;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Diagnostics;

/// <summary>
/// Checks a project's <c>.aiko</c> data against the baseline in <c>.aiko/manifest.json</c>, and records that
/// baseline when someone asks for it.
/// </summary>
/// <remarks>
/// Two questions, one report. The first is whether the data can be read at all: every project document is a
/// JSON file someone may have edited by hand, and a truncated one stops the whole project - reindexing reads
/// and validates before it writes, so a single unparsable file is why a board stops moving. The second is
/// whether anything changed since the baseline was sealed.
/// <para>
/// A change is not an error. Reports say "since the baseline" rather than "outside Aiko", because with the
/// baseline recorded deliberately there is no way to tell which of the two wrote the file - and a project whose
/// data lives in files expects its owner to write some of them. What the report gives is the fact and the path;
/// what to do about it is the reader's call, and accepting it is sealing again.
/// </para>
/// <para>
/// Service state is left out: <c>projections/</c> is rebuilt from the files, <c>runtime/</c> changes while a
/// stage runs, and leftover <c>.tmp</c> files are this code's own debris. Recording them would turn Aiko's own
/// work into a finding.
/// </para>
/// </remarks>
public sealed class FileProjectIntegrity(IProjectCatalog projects) : IProjectIntegrityCheck
{
    /// <summary>Shape version of the manifest this build writes.</summary>
    private const int CurrentSchemaVersion = 1;

    /// <inheritdoc />
    public async ValueTask<ProjectIntegrityReport> VerifyAsync(
        string projectId,
        CancellationToken cancellationToken) =>
        await InspectAsync(await FindAsync(projectId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ProjectIntegrityReport> SealAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindAsync(projectId, cancellationToken);
        var entries = new List<IntegrityEntry>();
        foreach (var file in Files(project.RootPath))
        {
            entries.Add(new IntegrityEntry(file.Relative, await HashAsync(file.Full, cancellationToken)));
        }

        var manifestPath = AikoProjectPaths.IntegrityManifest(project.RootPath);
        var (previous, _) = await ReadManifestAsync(manifestPath, cancellationToken);
        await WriteManifestAsync(
            project.RootPath,
            new IntegrityManifestDocument(
                CurrentSchemaVersion,
                previous?.Revision + 1 ?? 1,
                DateTimeOffset.UtcNow,
                entries),
            cancellationToken);

        // The answer is the check that follows the seal rather than the entries just written: a caller sees one
        // report shape either way, and a seal that did not take is visible in the same reply.
        return await InspectAsync(project, cancellationToken);
    }

    private async ValueTask<ProjectIntegrityReport> InspectAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        var manifestPath = AikoProjectPaths.IntegrityManifest(project.RootPath);
        var (baseline, baselineProblem) = await ReadManifestAsync(manifestPath, cancellationToken);

        var findings = new List<IntegrityFinding>();
        if (baselineProblem is not null)
        {
            findings.Add(new IntegrityFinding(
                IntegrityFindingKind.Document,
                DiagnosticSeverity.Error,
                Path.GetFileName(manifestPath),
                $"Cannot be read as the baseline ({baselineProblem}). Seal a new one to replace it."));
        }

        // Hash what is there and read every JSON document as it goes: the parse is the structural half of the
        // check, and doing it on bytes already in hand costs nothing extra.
        var onDisk = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files(project.RootPath))
        {
            var bytes = await File.ReadAllBytesAsync(file.Full, cancellationToken);
            onDisk[file.Relative] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (file.Relative.EndsWith(".json", StringComparison.Ordinal) && !IsJson(bytes))
            {
                findings.Add(new IntegrityFinding(
                    IntegrityFindingKind.Document,
                    DiagnosticSeverity.Error,
                    file.Relative,
                    "Not readable as JSON."));
            }
        }

        if (baseline is null)
        {
            findings.Add(new IntegrityFinding(
                IntegrityFindingKind.Document,
                DiagnosticSeverity.Warning,
                Path.GetFileName(manifestPath),
                "No baseline yet: run `aiko verify --seal` to record the current state as one."));
        }
        else
        {
            Compare(baseline, onDisk, findings);
        }

        // Errors first, then by path: what stops the project is read before what merely differs, and the order
        // does not depend on the order the file system happened to hand the files over.
        var ordered = findings
            .OrderBy(finding => (int)finding.Kind)
            .ThenBy(finding => finding.Path, StringComparer.Ordinal)
            .ToArray();

        return new ProjectIntegrityReport(
            project.Id,
            project.Name,
            project.Handle,
            baseline is not null,
            baseline?.SealedAt,
            onDisk.Count,
            ordered);
    }

    /// <summary>
    /// Turns the difference between the baseline and the disk into findings: what it lists and the disk does
    /// not, what differs, and what the disk has and it does not.
    /// </summary>
    private static void Compare(
        IntegrityManifestDocument baseline,
        IReadOnlyDictionary<string, string> onDisk,
        List<IntegrityFinding> findings)
    {
        var recorded = baseline.Entries.ToDictionary(
            entry => entry.Path,
            entry => entry.Sha256,
            StringComparer.Ordinal);

        foreach (var path in recorded.Keys.Where(path => !onDisk.ContainsKey(path)).Order(StringComparer.Ordinal))
        {
            findings.Add(new IntegrityFinding(
                IntegrityFindingKind.Missing,
                DiagnosticSeverity.Warning,
                path,
                "In the baseline, but gone from disk."));
        }

        foreach (var (path, hash) in onDisk)
        {
            if (!recorded.TryGetValue(path, out var sealedHash))
            {
                findings.Add(new IntegrityFinding(
                    IntegrityFindingKind.Added,
                    DiagnosticSeverity.Ok,
                    path,
                    "Added since the baseline."));
            }
            else if (!string.Equals(sealedHash, hash, StringComparison.Ordinal))
            {
                findings.Add(new IntegrityFinding(
                    IntegrityFindingKind.Changed,
                    DiagnosticSeverity.Warning,
                    path,
                    "Changed since the baseline."));
            }
        }
    }

    /// <summary>
    /// The project files the check and the seal cover, as paths relative to <c>.aiko</c> with forward slashes.
    /// </summary>
    /// <remarks>
    /// Everything under <c>.aiko</c> except service state: the documents of the project itself - the manifest of
    /// the project, its settings, its links, its relations, its releases, its workflows, the cards with their
    /// artifacts and discussions, and the memory a person may edit by hand.
    /// </remarks>
    private static IReadOnlyList<(string Relative, string Full)> Files(string projectRoot)
    {
        var dataRoot = AikoProjectPaths.DataRoot(projectRoot);
        if (!Directory.Exists(dataRoot))
        {
            return [];
        }

        var manifestPath = AikoProjectPaths.IntegrityManifest(projectRoot);
        return
        [
            .. Directory
                .EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
                .Where(file => !string.Equals(file, manifestPath, StringComparison.OrdinalIgnoreCase))
                .Select(file => (Relative: Relative(dataRoot, file), Full: file))
                .Where(file =>
                    !file.Relative.EndsWith(".tmp", StringComparison.Ordinal) &&
                    !InDirectory(file.Relative, AikoProjectPaths.ProjectionsDirectoryName) &&
                    !InDirectory(file.Relative, AikoProjectPaths.RuntimeDirectoryName))
                .OrderBy(file => file.Relative, StringComparer.Ordinal)
        ];
    }

    private static bool InDirectory(string relativePath, string directoryName) =>
        relativePath.StartsWith(directoryName + "/", StringComparison.Ordinal);

    private static string Relative(string dataRoot, string file) =>
        Path.GetRelativePath(dataRoot, file).Replace('\\', '/');

    /// <summary>
    /// Whether a document parses. A JSON document that does not is the failure this check exists for: the
    /// readers that rebuild the board throw on it, so nothing else in the project works until it is fixed.
    /// </summary>
    private static bool IsJson(byte[] bytes)
    {
        try
        {
            JsonDocument.Parse(bytes).Dispose();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async ValueTask<string> HashAsync(string path, CancellationToken cancellationToken) =>
        Convert.ToHexStringLower(
            SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));

    /// <summary>
    /// Reads the baseline, answering with the reason it could not be used when there is one and with null when
    /// there is simply no file yet.
    /// </summary>
    private static async ValueTask<(IntegrityManifestDocument? Document, string? Problem)> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            await using var input = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<IntegrityManifestDocument>(
                input,
                AikoJson.Project,
                cancellationToken);
            return document is null
                ? (null, "the document is empty")
                : (document, null);
        }
        catch (JsonException exception)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>
    /// Writes the baseline through a temporary file, so a check running while a seal happens reads either the
    /// previous baseline or the new one and never half of either.
    /// </summary>
    private static async ValueTask WriteManifestAsync(
        string projectRoot,
        IntegrityManifestDocument document,
        CancellationToken cancellationToken)
    {
        var path = AikoProjectPaths.IntegrityManifest(projectRoot);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(output, document, AikoJson.Project, cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async ValueTask<RegisteredProject> FindAsync(string projectId, CancellationToken cancellationToken) =>
        await projects.FindAsync(projectId, cancellationToken)
        ?? throw new KeyNotFoundException($"Unknown Aiko project: {projectId}");
}
