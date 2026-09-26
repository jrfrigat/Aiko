using System.Net;
using Aiko.Pwa.Resources;

namespace Aiko.Pwa.Services;

/// <summary>
/// Turns a transport failure into something a person can act on.
/// </summary>
/// <remarks>
/// The daemon authenticates local clients, so the first thing that happens to a browser that has not
/// been paired yet is a 401 - and the exception's own text is
/// "net_http_message_not_success_statuscode_reason, 401, Unauthorized", which says nothing about what
/// to do. The pairing step is <c>aiko ui</c>; say so.
/// A request the client itself gave up on reads the same way: "net_http_request_timedout, 100" names the
/// deadline the runtime enforced, not the problem, and the number in it is the deadline in seconds.
/// </remarks>
internal static class FailureText
{
    /// <summary>The runtime's own text for a request its deadline ended, ahead of the number it carries.</summary>
    private const string TimedOutMarker = "net_http_request_timedout";

    /// <summary>
    /// The message to show for <paramref name="exception"/>: the pairing hint for an unauthorised response,
    /// the deadline hint for a request the client gave up on, the exception's own text otherwise.
    /// </summary>
    public static string Describe(Exception exception) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
            ? Loc.Get("NotPairedHint")
            : IsTimedOut(exception)
                ? Loc.Get("DaemonTimeoutHint")
                : exception.Message;

    /// <summary>
    /// Whether the request ended because the client's own deadline passed rather than because the daemon
    /// refused it. Both shapes are checked, because where the deadline is enforced decides which one arrives:
    /// the browser handler reports an aborted request as a cancellation, and carries its own text as well.
    /// The chain is walked because a wrapper - <see cref="HttpRequestException"/> around the cancellation, say
    /// - is what reaches a caller that awaited a response.
    /// </summary>
    /// <param name="exception">The failure a call raised.</param>
    /// <returns>True when the client's deadline is what ended the request.</returns>
    private static bool IsTimedOut(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException ||
                current.Message.Contains(TimedOutMarker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
