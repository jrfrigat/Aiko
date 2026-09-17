using System.Globalization;
using System.Resources;

namespace Aiko.Pwa.Resources;

/// <summary>
/// Accessor for the localized UI strings.
/// </summary>
/// <remarks>
/// The neutral <c>Loc.resx</c> is English and is the fallback for every language we do not translate;
/// <c>Loc.ru.resx</c> carries the Russian strings. The UI culture is chosen from the browser's
/// language preferences at startup (see <c>Program.cs</c>), so the keys added here are all that is
/// needed to add a language: drop in <c>Loc.&lt;culture&gt;.resx</c> and list it in
/// <c>SatelliteResourceLanguages</c>.
/// </remarks>
public static class Loc
{
    private static readonly ResourceManager Manager = new("Aiko.Pwa.Resources.Loc", typeof(Loc).Assembly);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key itself when missing.
    /// </summary>
    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Returns the localized format string and applies <paramref name="args"/>.
    /// </summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}

