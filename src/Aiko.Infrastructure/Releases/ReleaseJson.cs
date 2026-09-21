using System.Text.Encodings.Web;
using System.Text.Json;

namespace Aiko.Infrastructure.Releases;

/// <summary>
/// The serializer options the release history is written with.
/// </summary>
/// <remarks>
/// The shape comes from the source-generated context, so the names, the indentation and the enum style cannot
/// drift from the other <c>.aiko</c> documents; only the encoder is stated here, for the reason
/// <c>AikoJson</c> states it for the project documents: a file meant to be opened by a person should show the
/// note they wrote as they wrote it and not as a wall of escape sequences.
/// </remarks>
internal static class ReleaseJson
{
    /// <summary>Options for <c>.aiko/releases.json</c>.</summary>
    internal static JsonSerializerOptions Document { get; } =
        new(ReleasesJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
