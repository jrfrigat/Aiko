using System.Text;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Embeddings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// A <c>tokenizer.json</c> as a model ships one, small enough to write inline, with the vocabulary chosen so
/// that each rule of the reader has something to be wrong about.
/// </summary>
internal sealed class TokenizerFixture : IDisposable
{
    private const string Tokens = """
        {
          "normalizer": {
            "type": "BertNormalizer",
            "clean_text": true,
            "handle_chinese_chars": true,
            "strip_accents": null,
            "lowercase": true
          },
          "model": {
            "type": "WordPiece",
            "unk_token": "[UNK]",
            "continuing_subword_prefix": "##",
            "max_input_chars_per_word": 100,
            "vocab": {
              "[PAD]": 0, "[UNK]": 100, "[CLS]": 101, "[SEP]": 102,
              ",": 1010,
              "the": 1000, "cat": 1001, "sits": 1002, "##ing": 1003, "sitt": 1004,
              "мама": 2000, "мыла": 2001, "раму": 2002,
              "привет": 3000,
              "cafe": 5000
            }
          },
          "added_tokens": []
        }
        """;

    private TokenizerFixture(string directory, HuggingFaceTokenizer value)
    {
        Directory = directory;
        Value = value;
    }

    public string Directory { get; }

    public HuggingFaceTokenizer Value { get; }

    public string TokenizerPath => Path.Combine(Directory, "tokenizer.json");

    /// <summary>Writes the tokenizer file and reads it back the way the provider reads it.</summary>
    public static TokenizerFixture Create(string? text = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aiko-tokenizer-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tokenizer.json");
        File.WriteAllText(path, text ?? Tokens, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new TokenizerFixture(directory, HuggingFaceTokenizer.FromFile(path));
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing spec.
        }
    }
}
