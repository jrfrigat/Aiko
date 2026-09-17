using System.Collections;
using System.Globalization;
using System.Resources;
using Aiko.Pwa.Resources;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the localization contract. The UI language follows the browser, and the neutral resources
/// are English - so a key that exists only in a satellite file is invisible to every visitor who does
/// not speak that language, and a satellite that stops shipping degrades its language to resource
/// keys. Both failures are silent at build time, which is why they are pinned here.
/// </summary>
public sealed class LocalizationSpecs
{
    private static readonly ResourceManager Manager = new("Aiko.Pwa.Resources.Loc", typeof(Loc).Assembly);

    [Fact]
    public void Every_translated_key_also_exists_in_the_neutral_resources()
    {
        var neutral = Keys(CultureInfo.InvariantCulture);
        var russian = Keys(new CultureInfo("ru"));

        // A satellite that is not copied next to the app would leave the Russian UI showing keys.
        Assert.NotEmpty(russian);

        var unreachable = russian.Except(neutral).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Assert.Empty(unreachable);
    }

    [Fact]
    public void The_neutral_resources_are_english()
    {
        // The neutral set is what every untranslated language reads, so it must not be a second copy
        // of a translation.
        var cyrillic = Keys(CultureInfo.InvariantCulture)
            .Where(key => Manager.GetString(key, CultureInfo.InvariantCulture)!
                .Any(character => character is >= '\u0400' and <= '\u04FF'))
            .ToArray();

        Assert.True(
            cyrillic.Length == 0,
            $"The neutral resources must be English; found Cyrillic in: {string.Join(", ", cyrillic)}");
    }

    [Fact]
    public void A_browser_language_selects_its_own_resources()
    {
        // Russian resolves to the satellite resources we ship.
        Assert.Equal("Закрыть", Manager.GetString("Close", new CultureInfo("ru-RU")));
        // Any other language resolves through the neutral English resources, region or not.
        Assert.Equal("Close", Manager.GetString("Close", CultureInfo.InvariantCulture));
        Assert.Equal("Close", Manager.GetString("Close", new CultureInfo("en-GB")));
        Assert.Equal("Close", Manager.GetString("Close", new CultureInfo("de-DE")));
    }

    /// <summary>
    /// The keys of one resource set. <c>tryParents: false</c> is the point: it reads the satellite's
    /// own set rather than the merged view the runtime would hand a lookup.
    /// </summary>
    private static HashSet<string> Keys(CultureInfo culture)
    {
        // Deliberately not disposed: ResourceManager caches this instance and hands it to every later
        // lookup, so disposing it poisons the rest of the process.
        var set = Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(set);
        return set!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}
