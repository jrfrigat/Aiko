namespace Aiko.Server.ErrorHandling;

/// <summary>
/// What a client may be told about a failure, and what belongs in the log instead.
/// </summary>
/// <remarks>
/// A message can describe the machine rather than the request: an <see cref="IOException"/> names the path it
/// could not touch, and a <see cref="System.Text.Json.JsonException"/> quotes the payload it could not read.
/// Both travel to whoever asked, and for a local daemon that is an agent or a page which has no business
/// learning the machine's layout - a leaked path is also the shape of the very injections those callers might
/// be trying (TASK-162).
/// </remarks>
internal static class ErrorText
{
    /// <summary>What a client gets instead of a message the daemon did not write.</summary>
    public const string SystemFailure = "The daemon could not carry out the request.";

    /// <summary>
    /// Whether the message was written by Aiko rather than by the runtime.
    /// </summary>
    /// <remarks>
    /// The split is by the origin of the text, not by its severity. A refusal the daemon wrote - an unknown
    /// card, a blocked start, a taken project id, a value that failed validation - is the answer an agent
    /// needs to act on, so it travels. <see cref="ArgumentException"/> is counted as ours on purpose: Aiko's
    /// own validation throws it, its message says which value was refused, and the runtime's own use of it
    /// (an illegal path character, say) names no directory. The types that describe the machine stay behind.
    /// </remarks>
    public static bool IsOurs(Exception exception) =>
        exception is not IOException and
        not UnauthorizedAccessException and
        not System.Text.Json.JsonException and
        not Microsoft.AspNetCore.Http.BadHttpRequestException;

    /// <summary>The message to answer with: the daemon's own words, or the generic text.</summary>
    public static string For(Exception exception) => IsOurs(exception) ? exception.Message : SystemFailure;
}
