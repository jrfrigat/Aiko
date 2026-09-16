namespace Aiko.Application.Contracts;

/// <summary>
/// Git policy for Aiko data (.aiko) inside the project repository.
/// </summary>
public enum ProjectGitPolicy
{
    /// <summary>Keep Aiko data local: ignore the whole .aiko directory.</summary>
    LocalOnly,

    /// <summary>Track project knowledge; ignore only runtime files.</summary>
    TrackProjectKnowledge,

    /// <summary>The user manages .gitignore manually; Aiko changes nothing.</summary>
    Custom
}
