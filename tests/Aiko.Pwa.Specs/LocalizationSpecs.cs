using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
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

    [Fact]
    public void The_language_list_matches_the_resources_and_the_script()
    {
        var satellites = SatelliteCultures();

        // A satellite that never got built would leave its language silent; an empty set would mean this
        // spec is checking nothing at all.
        Assert.NotEmpty(satellites);

        // English is the neutral set, so it is a language of its own with no satellite, while every other
        // language is exactly one of the satellites. Equality would be the wrong check: it would demand a
        // Loc.en.resx that must never exist.
        Assert.Contains(UiLanguages.Neutral, UiLanguages.Supported);
        Assert.Equal(
            satellites.OrderBy(code => code, StringComparer.Ordinal),
            UiLanguages.Supported
                .Where(code => !StringComparer.Ordinal.Equals(code, UiLanguages.Neutral))
                .OrderBy(code => code, StringComparer.Ordinal));

        // The script that runs before .NET cannot read the C# list, so it carries its own. Nothing else
        // notices when the two drift, and the drift shows as a document language and an error bar that
        // disagree with the rendered UI.
        Assert.Equal(
            UiLanguages.Supported.OrderBy(code => code, StringComparer.Ordinal),
            ShippedInScript().OrderBy(code => code, StringComparer.Ordinal));
    }

    [Fact]
    public void The_project_builds_every_satellite_the_list_promises()
    {
        // A .resx that is not copied next to the app is a translation nobody ever sees, and the build
        // succeeds either way: SatelliteResourceLanguages is the only thing that decides what ships.
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "Aiko.Pwa.csproj"));
        var declared = Regex.Match(
            project,
            "<SatelliteResourceLanguages>(?<list>[^<]*)</SatelliteResourceLanguages>");

        Assert.True(
            declared.Success,
            "Aiko.Pwa.csproj should name the satellites it builds, so a new translation ships with it.");

        Assert.Equal(
            SatelliteCultures().OrderBy(code => code, StringComparer.Ordinal),
            declared.Groups["list"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(code => code, StringComparer.Ordinal));
    }

    [Fact]
    public void The_saved_language_wins_then_the_browser_then_english()
    {
        // The saved choice decides, region and letter case included, whatever the browser prefers.
        Assert.Equal("ru", UiLanguages.Resolve(["en"], "ru").Name);
        Assert.Equal("ru", UiLanguages.Resolve([], "ru-RU").Name);
        Assert.Equal("en", UiLanguages.Resolve(["ru"], "EN").Name);
        // A region is stripped before the question is asked: what ships is the language, not the region.
        Assert.Equal("en", UiLanguages.Resolve(["ru"], "en-US-TX").Name);

        // A saved language we no longer ship is not remembered: honouring it would strand the interface
        // on resource keys, which is worse than the browser's own preference.
        Assert.Equal("ru", UiLanguages.Resolve(["ru"], "de").Name);

        // The browser's list is honoured in order, first language we ship wins - which is what the
        // platform asks for.
        Assert.Equal("ru", UiLanguages.Resolve(["de-DE", "ru-RU", "en"], saved: null).Name);

        // Nothing to go on, or nothing we can serve: the invariant culture, which resource lookup reads
        // as "use the neutral resources", i.e. English.
        Assert.Equal(CultureInfo.InvariantCulture, UiLanguages.Resolve([], saved: null));
        Assert.Equal(CultureInfo.InvariantCulture, UiLanguages.Resolve(["de", "fr"], " "));
        Assert.Equal(CultureInfo.InvariantCulture, UiLanguages.Resolve([], "fr-CA"));
    }

    [Fact]
    public void A_language_is_named_in_its_own_script()
    {
        // ICU names each language, so the switcher needs no translated names of its own: "English" and
        // "русский" read correctly whichever UI language is active, and adding a language adds no keys.
        Assert.Equal("English", UiLanguages.DisplayName("en"));
        Assert.Equal("Русский", UiLanguages.DisplayName("ru"));
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

    /// <summary>
    /// The cultures that have a satellite resource beside the neutral one, read from the resources
    /// folder - the languages the project is actually able to render.
    /// </summary>
    private static HashSet<string> SatelliteCultures() =>
        Directory
            .EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Resources"),
                "Loc.*.resx")
            .Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty)
            .Where(name => name.StartsWith("Loc.", StringComparison.Ordinal) && name.Length > 4)
            .Select(name => name[4..])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The languages the pre-.NET script lists in its own <c>shipped</c> array.
    /// </summary>
    private static string[] ShippedInScript()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "src", "Aiko.Pwa", "wwwroot", "js", "aiko-i18n.js");
        var declared = Regex.Match(File.ReadAllText(path), @"shipped\s*=\s*\[(?<list>[^\]]*)\]");

        Assert.True(
            declared.Success,
            "aiko-i18n.js should carry the languages it can render, as `shipped = [...]`.");

        return Regex
            .Matches(declared.Groups["list"].Value, "'(?<code>[^']*)'")
            .Select(item => item.Groups["code"].Value)
            .ToArray();
    }

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the markup specs do.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(LocalizationSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
