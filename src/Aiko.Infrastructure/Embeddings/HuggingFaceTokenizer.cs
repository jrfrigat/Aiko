using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Aiko.Infrastructure.Embeddings;

/// <summary>
/// The tokenizer a model ships with: reads its <c>tokenizer.json</c> and turns text into the token
/// identifiers its graph expects.
/// </summary>
/// <remarks>
/// Written here rather than taken from a package, and the reason is a fact that was checked rather than
/// assumed. The tokenizer library on offer reads a bare vocabulary file or a BPE pair, and this model's
/// second file is a <c>tokenizer.json</c> - the format that carries the vocabulary <em>and</em> the
/// normalisation the model was trained with. Reading only the vocabulary would tokenise differently from
/// the training data, and that does not fail: it produces a plausible vector for the wrong string. So the
/// normalisation belongs to this reader rather than to a step someone has to remember.
/// <para>
/// The pieces below follow the tokenizer the model was trained with: normalise, split on whitespace and
/// punctuation, then greedily match the longest vocabulary entry, with a continuation prefix for the pieces
/// after the first. A word that cannot be covered wholly becomes the unknown token - not the pieces that
/// did match, because half a word is a different word.
/// </para>
/// </remarks>
public sealed class HuggingFaceTokenizer
{
    private readonly Dictionary<string, int> vocabulary;
    private readonly int unknownId;
    private readonly int classificationId;
    private readonly int separatorId;
    private readonly bool lowercase;
    private readonly bool stripAccents;
    private readonly string continuationPrefix;
    private readonly int maxCharactersPerWord;

    private HuggingFaceTokenizer(
        Dictionary<string, int> vocabulary,
        int unknownId,
        int classificationId,
        int separatorId,
        int paddingId,
        bool lowercase,
        bool stripAccents,
        string continuationPrefix,
        int maxCharactersPerWord)
    {
        this.vocabulary = vocabulary;
        this.unknownId = unknownId;
        this.classificationId = classificationId;
        this.separatorId = separatorId;
        this.lowercase = lowercase;
        this.stripAccents = stripAccents;
        this.continuationPrefix = continuationPrefix;
        this.maxCharactersPerWord = maxCharactersPerWord;
        PaddingId = paddingId;
    }

    /// <summary>
    /// Identifier the graph reads where a batch is padded to a common length, and where the attention mask
    /// must therefore say "not a token".
    /// </summary>
    public int PaddingId { get; }

    /// <summary>
    /// Text of the token a word becomes when the vocabulary cannot cover it. Exposed for the same reason as
    /// <see cref="PaddingId"/>: a caller building input has to know what an unknown word looks like.
    /// </summary>
    public int UnknownId => unknownId;

    /// <summary>
    /// Reads a model's <c>tokenizer.json</c>.
    /// </summary>
    /// <param name="tokenizerPath">Path of the tokenizer file inside the model's directory.</param>
    /// <exception cref="FileNotFoundException">The file is not there, which means the model is not installed.</exception>
    /// <exception cref="InvalidDataException">The file is not a WordPiece tokenizer this reader understands.</exception>
    public static HuggingFaceTokenizer FromFile(string tokenizerPath)
    {
        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException(
                "The model's tokenizer is not installed, so text cannot be turned into tokens.",
                tokenizerPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(tokenizerPath));
        var root = document.RootElement;

        if (!root.TryGetProperty("model", out var model) ||
            !model.TryGetProperty("vocab", out var vocabElement))
        {
            throw new InvalidDataException(
                $"'{tokenizerPath}' has no 'model.vocab', so it is not a tokenizer this build reads.");
        }

        // The format names the algorithm, and one algorithm is not another: reading a different one as
        // WordPiece would produce tokens that are simply wrong, so it is refused rather than attempted.
        var modelType = model.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (!string.Equals(modelType, "WordPiece", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"'{tokenizerPath}' declares the model type '{modelType}', and this build reads WordPiece.");
        }

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in vocabElement.EnumerateObject())
        {
            if (entry.Value.TryGetInt32(out var id))
            {
                vocabulary[entry.Name] = id;
            }
        }

        var continuationPrefix =
            model.TryGetProperty("continuing_subword_prefix", out var prefix) &&
            prefix.GetString() is string prefixText
                ? prefixText
                : "##";

        return new HuggingFaceTokenizer(
            vocabulary,
            Special("[UNK]", "unk_token", 100),
            Special("[CLS]", "cls_token", 101),
            Special("[SEP]", "sep_token", 102),
            Special("[PAD]", "pad_token", 0),
            lowercase: ReadLowercase(root),
            stripAccents: ReadStripAccents(root),
            continuationPrefix: continuationPrefix,
            maxCharactersPerWord: model.TryGetProperty("max_input_chars_per_word", out var maxLength)
                ? maxLength.GetInt32()
                : 100);

        // The identifier of a special token: the name the format records for it when there is one, and the
        // conventional spelling otherwise. Never a hard-coded number - another model numbers them differently.
        int Special(string conventional, string recordedName, int fallback)
        {
            if (model.TryGetProperty(recordedName, out var recorded) &&
                recorded.GetString() is string recordedText &&
                vocabulary.TryGetValue(recordedText, out var recordedId))
            {
                return recordedId;
            }

            return vocabulary.TryGetValue(conventional, out var conventionalId) ? conventionalId : fallback;
        }
    }

    private static bool ReadLowercase(JsonElement root) =>
        root.TryGetProperty("normalizer", out var normalizer) &&
        normalizer.TryGetProperty("lowercase", out var lowercaseElement) &&
        lowercaseElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? lowercaseElement.GetBoolean()
            : true;

    /// <summary>
    /// Whether accents are removed. The format writes null to mean "follow the case setting", which is the
    /// convention this family of models uses, so null is read as "the same as lowercasing" and not as "no".
    /// </summary>
    private static bool ReadStripAccents(JsonElement root)
    {
        if (root.TryGetProperty("normalizer", out var normalizer) &&
            normalizer.TryGetProperty("strip_accents", out var accents) &&
            accents.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return accents.GetBoolean();
        }

        return ReadLowercase(root);
    }

    /// <summary>
    /// Turns text into the identifiers the graph reads, wrapped in the classification and separator tokens.
    /// </summary>
    /// <param name="text">Text to turn into tokens.</param>
    public IReadOnlyList<int> Encode(string text) => Encode(text, int.MaxValue);

    /// <summary>
    /// Turns text into identifiers, keeping at most <paramref name="maxTokens"/> of them.
    /// </summary>
    /// <remarks>
    /// Cut from the end and never from the middle: the classification token stays first and the separator
    /// last, because that is the shape the model was trained on. A sequence that starts where it should and
    /// stops early is still a sequence; one that stopped in the middle is not.
    /// </remarks>
    /// <param name="text">Text to turn into tokens.</param>
    /// <param name="maxTokens">Most identifiers to produce, both special tokens included.</param>
    public IReadOnlyList<int> Encode(string text, int maxTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 3);

        var tokens = new List<int> { classificationId };
        foreach (var word in PreTokenize(text ?? string.Empty))
        {
            AddWord(word, tokens);
        }

        // What is left of the budget after the two special tokens, and the tail beyond it is dropped.
        var contentLimit = maxTokens - 2;
        if (tokens.Count - 1 > contentLimit)
        {
            tokens.RemoveRange(1 + contentLimit, tokens.Count - 1 - contentLimit);
        }

        tokens.Add(separatorId);
        return tokens;
    }

    /// <summary>
    /// Turns one word into identifiers, longest-match first, and falls back to the unknown token.
    /// </summary>
    /// <remarks>
    /// The whole word becomes unknown when it cannot be covered, rather than the pieces that did match: a
    /// word reconstructed from some of its pieces is a different word, and pretending otherwise would feed
    /// the model a string nobody wrote.
    /// </remarks>
    private void AddWord(string word, List<int> sink)
    {
        if (word.Length == 0)
        {
            return;
        }

        if (word.Length > maxCharactersPerWord)
        {
            sink.Add(unknownId);
            return;
        }

        var pieces = new List<int>();
        var start = 0;
        while (start < word.Length)
        {
            var end = word.Length;
            var found = -1;
            while (start < end)
            {
                var candidate = start == 0
                    ? word[start..end]
                    : continuationPrefix + word[start..end];

                if (vocabulary.TryGetValue(candidate, out var id))
                {
                    found = id;
                    break;
                }

                end--;
            }

            if (found < 0)
            {
                sink.Add(unknownId);
                return;
            }

            pieces.Add(found);
            start = end;
        }

        sink.AddRange(pieces);
    }

    /// <summary>
    /// Splits text into the words the vocabulary is searched in: normalised, split on whitespace, with
    /// punctuation and Chinese characters standing as words of their own.
    /// </summary>
    /// <remarks>
    /// This is where a word boundary is decided, and getting it wrong is invisible: the vocabulary lookup
    /// still succeeds, it just matches a string the model never saw as a word.
    /// </remarks>
    private IEnumerable<string> PreTokenize(string text)
    {
        var word = new StringBuilder();
        foreach (var character in Normalize(text))
        {
            if (char.IsWhiteSpace(character))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                continue;
            }

            if (IsPunctuation(character) || IsChinese(character))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                yield return character.ToString();
                continue;
            }

            word.Append(character);
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    /// <summary>
    /// Applies the normalisation the model was trained with: drop control characters, collapse whitespace,
    /// and lowercase and strip accents as the file asks.
    /// </summary>
    private string Normalize(string text)
    {
        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            // Control characters are not text the model has ever seen. Tab, newline and carriage return are
            // kept as whitespace: they separate words, and dropping them would join two.
            if (character is '\u0000' or '\uFFFD' ||
                (char.IsControl(character) && character is not ('\t' or '\n' or '\r')))
            {
                continue;
            }

            cleaned.Append(char.IsWhiteSpace(character) ? ' ' : character);
        }

        var normalized = cleaned.ToString();
        if (lowercase)
        {
            normalized = normalized.ToLowerInvariant();
        }

        return stripAccents ? StripAccents(normalized) : normalized;
    }

    /// <summary>
    /// Removes combining marks, so that a letter carrying an accent matches the accented form in the
    /// vocabulary. Without it, accented text falls through to the unknown token in a way that looks like the
    /// vocabulary is missing words.
    /// </summary>
    private static string StripAccents(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Whether a character is punctuation by this tokenizer's rule: the ASCII punctuation ranges plus the
    /// Unicode punctuation categories.
    /// </summary>
    private static bool IsPunctuation(char character)
    {
        var code = (int)character;
        if (code is (>= 33 and <= 47) or (>= 58 and <= 64) or (>= 91 and <= 96) or (>= 123 and <= 126))
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(character)
            is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
    }

    /// <summary>
    /// Whether a character is a Chinese ideograph, which this tokenizer treats as a word by itself rather
    /// than gluing a run of them into one unknown token.
    /// </summary>
    private static bool IsChinese(char character) =>
        character is
            (>= '\u4E00' and <= '\u9FFF') or
            (>= '\u3400' and <= '\u4DBF') or
            (>= '\uF900' and <= '\uFAFF');
}
