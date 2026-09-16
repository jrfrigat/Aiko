using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project manifest in .aiko/project.json: identity and git policy.
/// </summary>
public sealed record ProjectManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string RootPath,
    ProjectGitPolicy GitPolicy,
    DateTimeOffset CreatedAt);
