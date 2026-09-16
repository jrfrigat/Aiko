using System.Text.Json;

namespace Aiko.Pwa.Services;

/// <summary>
/// JSON options for PWA HTTP calls: the source-generated <see cref="PwaJsonContext"/> (web defaults
/// plus string enums, matching the daemon's contracts) instead of reflection, because the published
/// client is trimmed. Pass these options to every <c>HttpClient</c> JSON call.
/// </summary>
public static class PwaJson
{
    /// <summary>
    /// Shared reusable options instance: the source-generated context's own options, so serialization
    /// never falls back to reflection (which the trimmed client cannot use).
    /// </summary>
    public static readonly JsonSerializerOptions Options = PwaJsonContext.Default.Options;
}
