using System.Text;

namespace Aiko.Application.Contracts;

/// <summary>
/// The human-readable handle of a project: derived from its name, editable while it is being created, and
/// unique among the registered projects.
/// </summary>
/// <remarks>
/// A project has two identifiers and they answer different questions. Its <c>Id</c> is a generated GUID: the
/// immutable key written into every <c>card.json</c>, every relation and every agent's MCP endpoint, which
/// is why cross-project references survive anything a user does to the visible name. Its slug is what a
/// person reads and types - <c>/p/aiko/board</c> instead of <c>/p/01a0b037d5b77a569e83aebe6d29a33a/board</c>.
/// Because the slug is derived from a name it can collide, so it is checked and, when a value was not typed
/// by hand, made unique with a numeric suffix rather than refusing to create the project.
/// </remarks>
public static class ProjectSlug
{
    /// <summary>Longest slug Aiko accepts; long enough for a name, short enough to stay readable in a URL.</summary>
    public const int MaxLength = 64;

    /// <summary>What a name that transliterates to nothing becomes, so a slug is never empty.</summary>
    public const string Fallback = "project";

    /// <summary>
    /// Cyrillic to Latin, one letter at a time: practically the GOST-style table a Russian reader expects,
    /// with <c>й</c> written as <c>j</c> (so "Мой" is <c>moj</c>) and the letters that have no sound of
    /// their own dropped.
    /// </summary>
    private static readonly Dictionary<char, string> Transliteration = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "j", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ъ'] = string.Empty, ['ы'] = "y", ['ь'] = string.Empty, ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // Ukrainian letters, so a project named in it is not mangled into dashes.
        ['і'] = "i", ['ї'] = "ji", ['є'] = "je", ['ґ'] = "g"
    };

    /// <summary>
    /// Builds a slug out of a display name: lower-case, Cyrillic transliterated, everything else that is not
    /// a latin letter or a digit turned into a single dash.
    /// </summary>
    /// <param name="name">Display name, for example <c>Мой пеРвыЙ проект</c>.</param>
    public static string Derive(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var raw = name.Trim().ToLowerInvariant();
        var mapped = new StringBuilder(raw.Length);
        foreach (var character in raw)
        {
            if (char.IsAsciiLetter(character) || char.IsAsciiDigit(character))
            {
                mapped.Append(character);
            }
            else if (Transliteration.TryGetValue(character, out var latin))
            {
                mapped.Append(latin);
            }
            else
            {
                // A space, punctuation, an emoji, a script with no table: all of them are a word break.
                mapped.Append('-');
            }
        }

        // Runs of dashes collapse and the edges lose theirs, so " - Hello,  world! - " is "hello-world".
        var slug = new StringBuilder(mapped.Length);
        foreach (var character in mapped.ToString())
        {
            if (character == '-' && (slug.Length == 0 || slug[^1] == '-'))
            {
                continue;
            }

            slug.Append(character);
        }

        var result = slug.ToString().TrimEnd('-');
        if (result.Length == 0)
        {
            return Fallback;
        }

        return result.Length <= MaxLength
            ? result
            : result[..MaxLength].TrimEnd('-');
    }

    /// <summary>
    /// Whether a value may be used as a slug: latin lower-case letters, digits and dashes, not starting or
    /// ending with one, and short enough for a URL.
    /// </summary>
    /// <param name="slug">Value to check.</param>
    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > MaxLength ||
            slug[0] == '-' || slug[^1] == '-')
        {
            return false;
        }

        return slug.All(character =>
            character == '-' ||
            (char.IsAsciiLetterOrDigit(character) && !char.IsAsciiLetterUpper(character)));
    }

    /// <summary>
    /// Returns the slug itself when it is free, otherwise the shortest <c>slug-2</c>, <c>slug-3</c>, ... that
    /// is free.
    /// </summary>
    /// <remarks>
    /// For a slug Aiko derived itself: a user who typed the value gets an error instead, but a user who only
    /// picked a folder should never be told their project cannot be created because the name is popular.
    /// </remarks>
    /// <param name="slug">Desired slug, normally the result of <see cref="Derive"/>.</param>
    /// <param name="isTaken">Whether a candidate already belongs to another project.</param>
    public static string MakeUnique(string slug, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        var baseSlug = IsValid(slug) ? slug : Fallback;
        var candidate = baseSlug;
        var suffix = 2;
        while (isTaken(candidate))
        {
            var suffixText = $"-{suffix++}";
            // Every candidate is built from the original slug rather than from the previous attempt: building
            // on the attempt would turn a second clash into "aiko-2-3" instead of "aiko-3".
            candidate = baseSlug.Length + suffixText.Length <= MaxLength
                ? $"{baseSlug}{suffixText}"
                : $"{baseSlug[..(MaxLength - suffixText.Length)].TrimEnd('-')}{suffixText}";
        }

        return candidate;
    }
}
