using System.Globalization;
using System.Resources;

namespace Aiko.Pwa.Resources;

/// <summary>
/// Accessor for the localized UI strings. The neutral Loc.resx is the fallback (empty for now);
/// Loc.ru.resx carries the Russian strings, which is the default UI language.
/// </summary>
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

