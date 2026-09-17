using System.Diagnostics;
using System.Globalization;
using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Git;

/// <summary>
/// The git client of the machine, run as a command.
/// </summary>
/// <remarks>
/// One process per question, no shell: the arguments are passed as a list, so a path with spaces or a quote
/// in it cannot turn into a different command. Every call has a timeout, because a git command waiting for a
/// credential prompt would otherwise hang the daemon's request thread.
/// <para>
/// Nothing here throws for a missing client or for a directory that is not a repository: those are answers a
/// screen shows ("Git client unavailable"), not failures of the daemon.
/// </para>
/// </remarks>
public sealed class GitClient : IGitClient
{
    internal const string ClientUnavailable = "Git client unavailable";
    internal const string NotARepository = "not a git repository";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public async ValueTask<GitStatus> StatusAsync(string directory, CancellationToken cancellationToken)
    {
        var version = await RunAsync(directory, ["--version"], cancellationToken);
        if (!version.Succeeded)
        {
            return new GitStatus(false, null, ClientUnavailable, false, null, null, null, 0, null, null);
        }

        var root = await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!root.Succeeded)
        {
            // Git is there and the directory is not a repository: the version line is still worth reporting.
            return new GitStatus(
                true,
                version.StandardOutput.Trim(),
                NotARepository,
                false,
                null,
                null,
                null,
                0,
                null,
                null);
        }

        var repositoryRoot = root.StandardOutput.Trim();
        var branch = await RunAsync(repositoryRoot, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        var remote = await RunAsync(repositoryRoot, ["remote", "get-url", "origin"], cancellationToken);
        var status = await RunAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);
        var head = await RunAsync(repositoryRoot, ["log", "-1", "--pretty=%h%x1f%s"], cancellationToken);

        var headParts = Split(head.StandardOutput, 2);
        var changed = status.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        return new GitStatus(
            true,
            version.StandardOutput.Trim(),
            null,
            true,
            repositoryRoot,
            // A detached HEAD prints "HEAD" as the branch name; the design's chip wants a branch or nothing.
            branch.Succeeded && !string.Equals(branch.StandardOutput.Trim(), "HEAD", StringComparison.Ordinal)
                ? branch.StandardOutput.Trim()
                : null,
            remote.Succeeded ? remote.StandardOutput.Trim() : null,
            changed,
            headParts.Length > 0 ? headParts[0] : null,
            headParts.Length > 1 ? headParts[1] : null);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GitCommit>> LogAsync(
        string directory,
        int count,
        CancellationToken cancellationToken)
    {
        // One record per line, fields separated by the unit separator: a commit message contains anything,
        // including the separators a person would have picked.
        var log = await RunAsync(
            directory,
            ["log", $"-n{Math.Clamp(count, 1, 100)}", "--pretty=%H%x1f%h%x1f%an%x1f%aI%x1f%s"],
            cancellationToken);
        if (!log.Succeeded)
        {
            return [];
        }

        var commits = new List<GitCommit>();
        foreach (var line in Lines(log.StandardOutput))
        {
            var parts = Split(line, 5);
            if (parts.Length < 5)
            {
                continue;
            }

            commits.Add(new GitCommit(
                parts[0],
                parts[1],
                parts[2],
                DateTimeOffset.TryParse(parts[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                    ? at
                    : DateTimeOffset.MinValue,
                parts[4]));
        }

        return commits;
    }

    /// <inheritdoc />
    public async ValueTask<GitCardDiff> DiffAsync(
        string directory,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return new GitCardDiff(true, null, []);
        }

        var status = await StatusAsync(directory, cancellationToken);
        if (!status.Available || !status.IsRepository)
        {
            return GitCardDiff.Unavailable(status.Failure ?? ClientUnavailable);
        }

        var root = status.RepositoryRoot!;
        // The paths are passed after `--`, so git reads them as pathspecs with its own wildcard rules: a
        // card that declared a glob is diffed by the same rule its scope matcher uses.

        // What the working tree has not committed yet is the first answer; a card whose files are all
        // committed falls back to the last commit that touched them, which is what the reader wants to see.
        var patch = await RunAsync(
            root,
            ["diff", "--no-color", "--unified=3", "HEAD", "--", .. paths],
            cancellationToken);
        var counts = await RunAsync(root, ["diff", "--numstat", "HEAD", "--", .. paths], cancellationToken);
        if (string.IsNullOrWhiteSpace(patch.StandardOutput))
        {
            patch = await RunAsync(
                root,
                ["show", "--no-color", "--unified=3", "--format=", "HEAD", "--", .. paths],
                cancellationToken);
            counts = await RunAsync(
                root,
                ["show", "--numstat", "--format=", "HEAD", "--", .. paths],
                cancellationToken);
        }

        return new GitCardDiff(true, null, ReadPatch(patch.StandardOutput, ReadNumstat(counts.StandardOutput)));
    }

    /// <summary>The added and removed counts per path, as <c>--numstat</c> prints them.</summary>
    private static IReadOnlyDictionary<string, (int Added, int Removed)> ReadNumstat(string output)
    {
        var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var line in Lines(output))
        {
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            // A binary file prints "-" for both counts: it has no lines to count.
            counts[parts[2]] = (
                int.TryParse(parts[0], CultureInfo.InvariantCulture, out var added) ? added : 0,
                int.TryParse(parts[1], CultureInfo.InvariantCulture, out var removed) ? removed : 0);
        }

        return counts;
    }

    /// <summary>
    /// Splits one patch into a file per <c>diff --git</c> header, so the screen can draw the design's file
    /// block with its own header and its own lines.
    /// </summary>
    private static IReadOnlyList<GitFileDiff> ReadPatch(
        string patch,
        IReadOnlyDictionary<string, (int Added, int Removed)> counts)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return [];
        }

        var files = new List<GitFileDiff>();
        var current = new List<string>();
        string? currentPath = null;
        var binary = false;

        void Flush()
        {
            if (currentPath is null)
            {
                return;
            }

            var (added, removed) = counts.TryGetValue(currentPath, out var count) ? count : (0, 0);
            files.Add(new GitFileDiff(currentPath, added, removed, binary ? string.Empty : string.Join('\n', current)));
            current.Clear();
            binary = false;
        }

        foreach (var line in patch.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush();
                currentPath = PathAfterDiffHeader(line);
                continue;
            }

            if (line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                binary = true;
            }

            current.Add(line);
        }

        Flush();
        return files;
    }

    /// <summary>The path a <c>diff --git a/&lt;path&gt; b/&lt;path&gt;</c> header names.</summary>
    private static string PathAfterDiffHeader(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return line;
        }

        var target = parts[3];
        return target.StartsWith("b/", StringComparison.Ordinal) ? target[2..] : target;
    }

    /// <summary>
    /// Runs git and reports what it said. A missing executable, a non-zero exit and a timeout are all
    /// reported as an unsuccessful run rather than thrown.
    /// </summary>
    private static async Task<GitRun> RunAsync(
        string directory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Directory.Exists(directory) ? directory : Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // -C then the directory, then the command and its arguments, one entry each: no shell, so nothing in
        // them is interpreted.
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(startInfo.WorkingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // A prompt (credentials, an unknown host key) would block forever, and this daemon has nobody to type.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return GitRun.Failed;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return new GitRun(process.ExitCode == 0, await standardOutput, await standardError);
        }
        catch (Exception)
        {
            // Win32Exception (no git on PATH), InvalidOperationException (no working directory), a timeout
            // and a cancelled request all mean the same thing here: git did not answer this question.
            return GitRun.Failed;
        }
    }

    /// <summary>Splits one record on the unit separator, keeping the last field whole.</summary>
    private static string[] Split(string line, int fields) => line.Trim().Split('\u001f', fields);

    private static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record GitRun(bool Succeeded, string StandardOutput, string StandardError)
    {
        public static GitRun Failed { get; } = new(false, string.Empty, string.Empty);
    }
}

