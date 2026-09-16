namespace Aiko.Application.Agents;

/// <summary>
/// Outcome of processing a single file during installation or uninstallation.
/// </summary>
public enum InstallationFileStatus
{
    /// <summary>The file was created.</summary>
    Created,

    /// <summary>The file was updated.</summary>
    Updated,

    /// <summary>The file was removed.</summary>
    Removed,

    /// <summary>No changes were required.</summary>
    Unchanged,

    /// <summary>Processing the file failed.</summary>
    Failed
}
