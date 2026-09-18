using Aiko.Application.Agents;
using Aiko.Domain.Execution;

namespace Aiko.Infrastructure.Agents;

/// <summary>
/// Base class of the built-in agent adapters: shared logic for installation discovery,
/// planning, applying and removing project-scoped configuration through the declarative
/// file descriptions of <see cref="AgentFileDefinition"/>.
/// </summary>
public abstract class BuiltInAgentAdapter : IAgentAdapter
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract AgentCapabilities Capabilities { get; }

    /// <summary>
    /// Executable names of the agent searched for in PATH.
    /// </summary>
    protected abstract string[] ExecutableNames { get; }

    /// <inheritdoc />
    public virtual ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installations = ExecutableDetector.Find(ExecutableNames)
            .Select(path => new AgentInstallation(
                $"{Id}:{path}",
                Id,
                path,
                null))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<AgentInstallation>>(installations);
    }

    /// <inheritdoc />
    public ValueTask<InstallationPlan> PlanProjectInstallAsync(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Uri.TryCreate(projectMcpEndpoint, UriKind.Absolute, out var endpoint) ||
            !endpoint.IsLoopback)
        {
            throw new ArgumentException(
                "The project MCP endpoint must be an absolute loopback URL.",
                nameof(projectMcpEndpoint));
        }

        return ValueTask.FromResult(new InstallationPlan(
            Id,
            CreateFiles(Path.GetFullPath(projectRoot), projectMcpEndpoint, accessToken, cardTypes)
                .Select(file => new InstallationChange(file.Path, file.Description))
                .ToArray(),
            CreateWarnings()));
    }

    /// <inheritdoc />
    public ValueTask<bool> IsProjectConfiguredAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            OwnedDirectories(Path.GetFullPath(projectRoot))
                .SelectMany(FindOwnedFiles)
                .Any());
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes,
        CancellationToken cancellationToken)
    {
        await PlanProjectInstallAsync(projectRoot, projectMcpEndpoint, accessToken, cardTypes, cancellationToken);
        var fullRoot = Path.GetFullPath(projectRoot);
        var definitions = CreateFiles(fullRoot, projectMcpEndpoint, accessToken, cardTypes);
        var files = new List<InstallationFileResult>();

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                files.Add(await AgentConfigurationWriter.ApplyAsync(
                    definition,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                files.Add(new InstallationFileResult(
                    definition.Path,
                    InstallationFileStatus.Failed,
                    exception.Message));
            }
        }

        // A command is derived from a card type or a stage, so an owned file the plan no longer describes is
        // a leftover - of a removed type, or of an older Aiko that named its commands differently. Removing
        // it here keeps the two in step without a recorded manifest to go stale.
        files.AddRange(await PruneOwnedFilesAsync(
            OwnedDirectories(fullRoot),
            definitions.Select(definition => definition.Path).ToArray(),
            cancellationToken));

        return new AgentInstallationResult(
            Id,
            files.All(file => file.Status is not InstallationFileStatus.Failed),
            files,
            CreateWarnings());
    }

    /// <inheritdoc />
    public ValueTask<InstallationPlan> PlanProjectUninstallAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullRoot = Path.GetFullPath(projectRoot);
        var directories = OwnedDirectories(fullRoot);
        // Every owned file is reported, not only the ones a current plan names: a command of a card type the
        // project has since removed is exactly what a user wants to see going away.
        var planned = CreateFiles(fullRoot, "http://127.0.0.1/mcp", null, [])
            .Select(file => file.Path)
            .Concat(directories.SelectMany(FindOwnedFiles))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ValueTask.FromResult(new InstallationPlan(
            Id,
            planned
                .Select(path => new InstallationChange(
                    path,
                    "Remove the Aiko-owned file if its ownership marker is present."))
                .ToArray(),
            CreateWarnings()));
    }
    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> UninstallProjectAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        await PlanProjectUninstallAsync(projectRoot, cancellationToken);
        var fullRoot = Path.GetFullPath(projectRoot);
        var files = new List<InstallationFileResult>();
        foreach (var definition in CreateFiles(fullRoot, "http://127.0.0.1/mcp", null, []))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                files.Add(await AgentConfigurationWriter.RemoveAsync(
                    definition,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                files.Add(new InstallationFileResult(
                    definition.Path,
                    InstallationFileStatus.Failed,
                    exception.Message));
            }
        }

        // Commands of card types and of an older Aiko are not in the list above - that list is what the
        // current build would install - so the owned files are swept as well.
        files.AddRange(await PruneOwnedFilesAsync(
            OwnedDirectories(fullRoot),
            [],
            cancellationToken));

        return new AgentInstallationResult(
            Id,
            files.All(file => file.Status is not InstallationFileStatus.Failed),
            files,
            CreateWarnings());
    }

    /// <inheritdoc />
    public ValueTask<InstallationPlan> PlanUserInstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new InstallationPlan(
            Id,
            CreateUserFiles().Select(file => new InstallationChange(file.Path, file.Description)).ToArray(),
            CreateWarnings()));
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> ApplyUserInstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<InstallationFileResult>();
        foreach (var definition in CreateUserFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                files.Add(await AgentConfigurationWriter.ApplyAsync(definition, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                files.Add(new InstallationFileResult(
                    definition.Path,
                    InstallationFileStatus.Failed,
                    exception.Message));
            }
        }

        return new AgentInstallationResult(
            Id,
            files.All(file => file.Status is not InstallationFileStatus.Failed),
            files,
            CreateWarnings());
    }

    /// <inheritdoc />
    public ValueTask<InstallationPlan> PlanUserUninstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new InstallationPlan(
            Id,
            CreateUserFiles()
                .Select(file => new InstallationChange(
                    file.Path,
                    "Remove the Aiko-owned file if its ownership marker is present."))
                .ToArray(),
            CreateWarnings()));
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> UninstallUserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<InstallationFileResult>();
        foreach (var definition in CreateUserFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                files.Add(await AgentConfigurationWriter.RemoveAsync(definition, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                files.Add(new InstallationFileResult(
                    definition.Path,
                    InstallationFileStatus.Failed,
                    exception.Message));
            }
        }

        return new AgentInstallationResult(
            Id,
            files.All(file => file.Status is not InstallationFileStatus.Failed),
            files,
            CreateWarnings());
    }

    /// <inheritdoc />
    public ValueTask<AgentAttemptState> ClassifyExitAsync(
        int exitCode,
        string standardError,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rateLimited =
            standardError.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("quota", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(
            rateLimited
                ? AgentAttemptState.RateLimited
                : exitCode == 0
                    ? AgentAttemptState.Completed
                    : AgentAttemptState.Failed);
    }

    /// <summary>
    /// Returns descriptions of the configuration files the adapter creates in the project.
    /// </summary>
    /// <param name="projectRoot">Full path to the project root.</param>
    /// <param name="projectMcpEndpoint">Absolute loopback URL of the project MCP server.</param>
    /// <param name="accessToken">The daemon's access token, written into the MCP entry so the client can authenticate.</param>
    /// <param name="cardTypes">
    /// Card types the project defines, for the adapters that derive a command per type. An adapter without
    /// commands ignores it.
    /// </param>
    private protected abstract IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        IReadOnlyList<CardTypeDescriptor> cardTypes);

    /// <summary>
    /// A directory whose files Aiko owns by name, so entries are found without a recorded manifest.
    /// </summary>
    /// <param name="Path">Absolute path of the directory.</param>
    /// <param name="SearchPattern">Glob of the names Aiko may own inside it.</param>
    private protected sealed record OwnedDirectory(string Path, string SearchPattern);

    /// <summary>
    /// The directories Aiko manages by name in this project; empty when the adapter owns no files that way.
    /// </summary>
    /// <remarks>
    /// Derived from the project root rather than from the install plan, because their purpose is to find the
    /// files a plan no longer mentions - the commands of card types the project has since removed, and of
    /// command names an older Aiko used.
    /// </remarks>
    /// <param name="projectRoot">Full path to the project root.</param>
    private protected virtual IReadOnlyList<OwnedDirectory> OwnedDirectories(string projectRoot) => [];

    /// <summary>
    /// Finds the files of a managed directory that carry Aiko's ownership marker.
    /// </summary>
    /// <param name="directory">Managed directory to scan.</param>
    private protected static IReadOnlyList<string> FindOwnedFiles(OwnedDirectory directory) =>
        Directory.Exists(directory.Path)
            ? Directory.EnumerateFiles(directory.Path, directory.SearchPattern)
                .Where(file => File.ReadAllText(file).Contains(
                    AgentFileMarkers.Managed,
                    StringComparison.Ordinal))
                .ToArray()
            : [];

    /// <summary>
    /// Removes the owned files a plan does not describe, which is how a command whose card type is gone
    /// disappears instead of lingering; a file without the ownership marker is never touched.
    /// </summary>
    /// <param name="directories">Managed directories to sweep.</param>
    /// <param name="plannedPaths">Paths the current plan describes, and therefore keeps.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private protected static ValueTask<IReadOnlyList<InstallationFileResult>> PruneOwnedFilesAsync(
        IReadOnlyList<OwnedDirectory> directories,
        IReadOnlyCollection<string> plannedPaths,
        CancellationToken cancellationToken)
    {
        var removed = new List<InstallationFileResult>();
        foreach (var file in directories.SelectMany(FindOwnedFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plannedPaths.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(file);
            removed.Add(new InstallationFileResult(file, InstallationFileStatus.Removed, null));
        }

        return ValueTask.FromResult<IReadOnlyList<InstallationFileResult>>(removed);
    }

    /// <summary>
    /// Returns descriptions of the user-scoped (global) configuration files the adapter
    /// creates in the user home directory. Empty when the adapter has no global scope.
    /// </summary>
    private protected virtual IReadOnlyList<AgentFileDefinition> CreateUserFiles() => [];

    /// <summary>
    /// Combines path segments under the current user's home directory. The home directory
    /// can be overridden with the AIKO_USER_HOME environment variable (used by tests and
    /// portable installs).
    /// </summary>
    private protected static string UserPath(params string[] parts) =>
        parts.Aggregate(HomeDirectory(), Path.Combine);

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable("AIKO_USER_HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Returns warnings shown to the user together with the plan; empty by default.
    /// </summary>
    protected virtual IReadOnlyList<string> CreateWarnings() => [];
}
