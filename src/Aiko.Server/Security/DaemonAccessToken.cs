namespace Aiko.Server.Security;

/// <summary>
/// The access token the running daemon authenticates with, available to the parts that write agent
/// configurations.
/// </summary>
/// <remarks>
/// The daemon resolves its token once at startup - from <c>AIKO_TOKEN</c> when set, otherwise from the
/// token file. Agent configurations have to carry that same value, and re-reading the file would be wrong
/// whenever the environment overrode it. A holder is used rather than a plain registration because the
/// token is only known after the host is built.
/// </remarks>
internal sealed class DaemonAccessToken
{
    /// <summary>The token the middleware checks, or null before startup finished.</summary>
    public string? Value { get; set; }
}
