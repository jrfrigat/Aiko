using System.Globalization;

namespace Aiko.Pwa.Resources;

/// <summary>
/// The languages the interface ships, and the rule that picks one before the first frame renders.
/// </summary>
/// <remarks>
/// This is the single place in code where the languages are listed. The appearance screen builds its
/// switcher from <see cref="Supported"/> and the startup resolution in <c>Program.cs</c> chooses from
/// the same list, so neither the markup nor the runtime keeps a second copy of it.
/// <para>
/// The language names are not resource keys either: they come from ICU, which names every language in
/// its own script - so a switcher needs no translated names, and both languages read correctly
/// whichever UI language happens to be active.
/// </para>
/// <para>
/// One relation is worth stating plainly, because it is easy to get backwards: the neutral
/// <c>Loc.resx</c> is English, so English is a language with no satellite assembly, while every other
/// language is one. <see cref="Supported"/> is therefore the neutral language plus the satellites the
/// project builds, and the specs check exactly that - in both directions, and against the project
/// file, so a language that is added in one place and forgotten in another fails the suite instead of
/// degrading a UI to resource keys.
/// </para>
/// </remarks>
public static class UiLanguages
{
    /// <summary>
    /// The key the chosen language is remembered under, in the browser's local storage.
    /// </summary>
    /// <remarks>
    /// Shared with <c>aiko-i18n.js</c> on purpose: that script settles the document language before
    /// .NET starts, and this app settles the culture of the first frame. Reading one stored value is
    /// what keeps the two from disagreeing, which is the flash neither of them can undo.
    /// </remarks>
    public const string PreferenceKey = "aiko-language";

    /// <summary>
    /// The language of the neutral resources: the fallback for everything untranslated, and a choice
    /// of its own in the switcher.
    /// </summary>
    public const string Neutral = "en";

    /// <summary>Every language the interface can render in.</summary>
    public static IReadOnlyList<string> Supported { get; } = [Neutral, "ru"];

    /// <summary>
    /// The culture the interface renders in: the saved choice when it is one we still ship, otherwise
    /// the first browser preference we ship, otherwise the invariant culture - which resource lookup
    /// reads as "use the neutral resources", i.e. English.
    /// </summary>
    /// <param name="preferred">The browser's language preferences, in the order it states them.</param>
    /// <param name="saved">The language chosen in the interface, or null when none was.</param>
    /// <remarks>
    /// A saved language we no longer ship is ignored rather than honoured: a satellite that was removed
    /// must not strand the interface on raw resource keys, and the visitor's own browser preference is
    /// a better answer than the one they gave for a language that is gone.
    /// </remarks>
    public static CultureInfo Resolve(IReadOnlyList<string> preferred, string? saved)
    {
        if (Normalize(saved) is { } remembered && IsSupported(remembered))
        {
            return CultureInfo.GetCultureInfo(remembered);
        }

        foreach (var language in preferred)
        {
            if (Normalize(language) is { } candidate && IsSupported(candidate))
            {
                return CultureInfo.GetCultureInfo(candidate);
            }
        }

        return CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Whether a language is one the interface ships. A region is allowed in the question
    /// (<c>ru-RU</c>) and ignored in the answer, because what ships is the language.
    /// </summary>
    public static bool IsSupported(string? language) =>
        Normalize(language) is { } code && Supported.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// A language's own name, for a switcher that should read the same in every UI language.
    /// </summary>
    public static string DisplayName(string language)
    {
        var native = CultureInfo.GetCultureInfo(language).NativeName;
        return native.Length == 0
            ? language
            : char.ToUpper(native[0], CultureInfo.InvariantCulture) + native[1..];
    }

    /// <summary>
    /// The language code of a region-tagged tag, in lower case, or null when there is no code in it.
    /// </summary>
    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var tag = language.Trim();
        var separator = tag.IndexOf('-');
        var code = (separator >= 0 ? tag[..separator] : tag).ToLowerInvariant();
        return code.Length == 0 ? null : code;
    }
}
