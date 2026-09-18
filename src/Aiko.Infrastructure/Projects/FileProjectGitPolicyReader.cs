using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Storage;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Reads the git policy from the project's own manifest: <c>.aiko/project.json</c>.
/// </summary>
/// <remarks>
/// Read-only and forgiving on purpose. An agent asks this to learn whether to commit, and a project whose
/// manifest is gone or unreadable must get "unknown" rather than the wrong policy - committing into a project
/// that had asked to stay local is exactly the mistake this question exists to prevent.
/// </remarks>
public sealed class FileProjectGitPolicyReader : IProjectGitPolicyReader
{
    /// <inheritdoc />
    public async ValueTask<ProjectGitPolicy?> ReadAsync(
        RegisteredProject project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = Path.Combine(AikoProjectPaths.DataRoot(project.RootPath), "project.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var input = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync(
                input,
                ProjectJsonContext.Default.ProjectManifest,
                cancellationToken);
            return manifest?.GitPolicy;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
