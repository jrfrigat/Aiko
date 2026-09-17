namespace Aiko.Application.Agents;

/// <summary>
/// How much of an adapter's user-scope Aiko configuration (the <c>/aiko-*</c> skills and commands written
/// into the user's home) is present on this machine.
/// </summary>
/// <remarks>
/// "The agent is installed" and "Aiko is connected to the agent" are independent facts: the executable
/// comes from the user's PATH, the configuration from Aiko. The dashboard reports both, so a detected
/// agent that was never connected is no longer mistaken for a missing one.
/// </remarks>
/// <param name="ConfiguredFiles">How many of the adapter's user-scope files exist.</param>
/// <param name="ExpectedFiles">How many the adapter would write; zero when it has no user scope.</param>
public sealed record AgentUserScope(int ConfiguredFiles, int ExpectedFiles)
{
    /// <summary>Whether every file the adapter writes is present.</summary>
    public bool IsConfigured() => ExpectedFiles > 0 && ConfiguredFiles == ExpectedFiles;

    /// <summary>Whether some, but not all, of them are present - a partial or outdated installation.</summary>
    public bool IsPartial() => ConfiguredFiles > 0 && ConfiguredFiles < ExpectedFiles;
}
