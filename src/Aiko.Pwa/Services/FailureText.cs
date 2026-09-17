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
/// </remarks>
internal static class FailureText
{
    /// <summary>
    /// The message to show for <paramref name="exception"/>: the pairing hint for an unauthorised
    /// response, the exception's own text otherwise.
    /// </summary>
    public static string Describe(Exception exception) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
            ? Loc.Get("NotPairedHint")
            : exception.Message;
}
