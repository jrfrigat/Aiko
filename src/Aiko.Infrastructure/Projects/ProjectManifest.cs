using Aiko.Application.Contracts;

namespace Aiko.Infrastructure.Projects;

/// <summary>
/// Project manifest in .aiko/project.json: identity, git policy and the template the project was created
/// from.
/// </summary>
/// <param name="SchemaVersion">Schema version of the file.</param>
/// <param name="Id">Stable project id.</param>
/// <param name="Name">Display name.</param>
/// <param name="RootPath">Project root directory.</param>
/// <param name="GitPolicy">Gitignore policy applied at init.</param>
/// <param name="CreatedAt">When the project was first initialized.</param>
/// <param name="TemplateId">Template the project was created from; null on manifests written before templates.</param>
/// <param name="TemplateVersion">Version of that template at the moment of creation.</param>
public sealed record ProjectManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string RootPath,
    ProjectGitPolicy GitPolicy,
    DateTimeOffset CreatedAt,
    string? TemplateId = null,
    int? TemplateVersion = null);
