namespace Aiko.Application.Contracts;

/// <summary>
/// Where a person opens a card artifact: the machine's file manager, or its code editor.
/// </summary>
/// <remarks>
/// A closed set rather than a command line. The daemon starts a local program on behalf of a browser, so
/// the caller names one of these and never a program of its own choosing.
/// </remarks>
public enum ArtifactOpenTarget
{
    /// <summary>The system file manager, at the folder that holds the file.</summary>
    Explorer,

    /// <summary>Visual Studio Code, with the file open.</summary>
    VisualStudioCode
}
