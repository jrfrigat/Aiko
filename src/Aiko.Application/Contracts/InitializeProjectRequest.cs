namespace Aiko.Application.Contracts;

/// <summary>
/// Request to initialize an Aiko project in a given directory.
/// </summary>
public sealed record InitializeProjectRequest(
    string RootPath,
    string? Name = null,
    ProjectGitPolicy GitPolicy = ProjectGitPolicy.LocalOnly);
