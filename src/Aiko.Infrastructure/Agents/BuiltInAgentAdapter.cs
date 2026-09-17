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
    public ValueTask<IReadOnlyList<AgentInstallation>> DetectInstallationsAsync(
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
            CreateFiles(Path.GetFullPath(projectRoot), projectMcpEndpoint, accessToken)
                .Select(file => new InstallationChange(file.Path, file.Description))
                .ToArray(),
            CreateWarnings()));
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> ApplyProjectInstallAsync(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        await PlanProjectInstallAsync(projectRoot, projectMcpEndpoint, accessToken, cancellationToken);
        var fullRoot = Path.GetFullPath(projectRoot);
        var files = new List<InstallationFileResult>();

        foreach (var definition in CreateFiles(fullRoot, projectMcpEndpoint, accessToken))
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
        var files = CreateFiles(fullRoot, "http://127.0.0.1/mcp", null);
        return ValueTask.FromResult(new InstallationPlan(
            Id,
            files
                .Select(file => new InstallationChange(
                    file.Path,
                    file.Kind == AgentFileKind.OwnedText
                        ? "Remove the Aiko-owned file if its ownership marker is present."
                        : "Remove only the Aiko-managed configuration entry or block."))
                .ToArray(),
            CreateWarnings()));
    }

    /// <inheritdoc />
    public async ValueTask<AgentInstallationResult> UninstallProjectAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        await PlanProjectUninstallAsync(projectRoot, cancellationToken);
        var files = new List<InstallationFileResult>();
        foreach (var definition in CreateFiles(
                     Path.GetFullPath(projectRoot),
                     "http://127.0.0.1/mcp",
                     null))
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
    private protected abstract IReadOnlyList<AgentFileDefinition> CreateFiles(
        string projectRoot,
        string projectMcpEndpoint,
        string? accessToken);

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
