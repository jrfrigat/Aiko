namespace Aiko.Application.Contracts;

/// <summary>
/// Request to initialize an Aiko project in a given directory. A null <c>GitPolicy</c> takes the chosen
/// template's own - a caller that picked a template already said what the project should start as - and a null
/// <c>TemplateId</c> takes <see cref="ProjectTemplate.DefaultId"/>, which is copied into the project here and
/// never read again. A <c>Slug</c> that was given and is already taken is an error, because the caller chose
/// it; a derived one is made unique silently.
/// </summary>
public sealed record InitializeProjectRequest(
    string RootPath,
    string? Name = null,
    ProjectGitPolicy? GitPolicy = null,
    string? TemplateId = null,
    string? Slug = null);
