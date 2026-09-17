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
/// <param name="Slug">
/// Human-readable handle used in the UI's URLs, or null on a project created before slugs existed. It lives
/// here as well as in the catalog so it survives a deleted database: re-registering the path restores the
/// same readable address instead of inventing a new one.
/// </param>
public sealed record ProjectManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string RootPath,
    ProjectGitPolicy GitPolicy,
    DateTimeOffset CreatedAt,
    string? TemplateId = null,
    int? TemplateVersion = null,
    string? Slug = null);
