using System.Text.Encodings.Web;
using System.Text.Json;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Settings;

namespace Aiko.Infrastructure.Storage;

/// <summary>
/// The serializer options the daemon writes its own documents with.
/// </summary>
/// <remarks>
/// A <c>.aiko</c> tree is meant to be read, diffed and reviewed by people, so its text keeps its
/// characters: the default encoder escapes every non-ASCII rune, which turns a Russian title into a wall of
/// <c>\u0442</c> and makes a diff of a translated file useless. Everything else - the camelCase names, the
/// indentation, the string enum values - is copied from the source-generated context rather than restated
/// here, so the two cannot drift apart.
/// </remarks>
internal static class AikoJson
{
    /// <summary>Options for the project documents: <c>.aiko/**/*.json</c>.</summary>
    internal static JsonSerializerOptions Project { get; } = WithReadableText(ProjectJsonContext.Default.Options);

    /// <summary>Options for a settings document, at either level.</summary>
    internal static JsonSerializerOptions Settings { get; } = WithReadableText(SettingsJsonContext.Default.Options);

    private static JsonSerializerOptions WithReadableText(JsonSerializerOptions source) =>
        new(source) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
