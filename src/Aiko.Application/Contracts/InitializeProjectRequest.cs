namespace Aiko.Application.Contracts;

/// <summary>
/// Request to initialize an Aiko project in a given directory.
/// </summary>
/// <param name="RootPath">Absolute path of the project root.</param>
/// <param name="Name">Display name; the directory name when omitted.</param>
/// <param name="GitPolicy">
/// Which gitignore policy to apply to the new project, or null to take the one the chosen template carries.
/// Null is the default because a caller that picked a template already said what the project should start
/// as, and asking it twice is how the two answers end up disagreeing.
/// </param>
/// <param name="TemplateId">
/// Template to create the project from, or null for <see cref="ProjectTemplate.DefaultId"/>. The chosen
/// template is copied into the project here and never read again: a later change to a template applies to
/// the projects created afterwards.
/// </param>
public sealed record InitializeProjectRequest(
    string RootPath,
    string? Name = null,
    ProjectGitPolicy? GitPolicy = null,
    string? TemplateId = null);
