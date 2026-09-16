using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aiko.Pwa.Services;

/// <summary>
/// JSON options for PWA HTTP calls: web defaults plus string enums,
/// matching the daemon's source-generated contracts.
/// </summary>
public static class PwaJson
{
    /// <summary>
    /// Shared reusable options instance.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
