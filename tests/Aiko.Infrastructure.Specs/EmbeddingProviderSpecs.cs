using System.Text;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Embeddings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The embedding provider: the tokenizer, the pooling, and the failure a user meets when the model is absent.
/// </summary>
/// <remarks>
/// Nothing here downloads or loads the real model - that file weighs 118 MB and a spec that needed it would
/// be a spec nobody runs. What is checked is everything around the graph: that text becomes the identifiers
/// the model was trained to see, that the pooling ignores padding, that a vector is normalised, and that a
/// missing model says so instead of returning a vector that means nothing.
/// <para>
/// What is deliberately <em>not</em> checked here is whether the pooling matches the model's training. That
/// is a comparison against a reference vector and belongs to a person looking at the numbers; a test would
/// only assert that the code does what the code does.
/// </para>
/// </remarks>
public sealed class EmbeddingProviderSpecs
{
    [Fact]
    public void Text_is_wrapped_in_the_classification_and_separator_tokens()
    {
        using var tokenizer = TokenizerFixture.Create();

        var tokens = tokenizer.Value.Encode("the cat sits");

        Assert.Equal([101, 1000, 1001, 1002, 102], tokens);
    }

    [Fact]
    public void A_word_the_vocabulary_covers_in_pieces_is_split_into_them()
    {
        using var tokenizer = TokenizerFixture.Create();

        // 'sitting' is not in the vocabulary, 'sitt' and '##ing' are: the word is covered by what it is made of.
        var tokens = tokenizer.Value.Encode("sitting");

        Assert.Equal([101, 1004, 1003, 102], tokens);
    }

    [Fact]
    public void A_word_the_vocabulary_cannot_cover_becomes_unknown_in_whole()
    {
        using var tokenizer = TokenizerFixture.Create();

        // 'cat' matches but '##s' does not, so the word is not 'cat' plus noise: it is one unknown word.
        var tokens = tokenizer.Value.Encode("cats");

        Assert.Equal([101, 100, 102], tokens);
    }

    [Fact]
    public void Uppercase_and_accents_are_normalised_to_what_the_vocabulary_holds()
    {
        using var tokenizer = TokenizerFixture.Create();

        // The file records strip_accents as null, which means "follow the case setting" - and the case
        // setting is on, so 'CAFÉ' has to arrive at the vocabulary's 'cafe' rather than at the unknown token.
        var tokens = tokenizer.Value.Encode("CAFÉ");

        Assert.Equal([101, 5000, 102], tokens);
    }

    [Fact]
    public void Punctuation_stands_as_its_own_token()
    {
        using var tokenizer = TokenizerFixture.Create();

        var tokens = tokenizer.Value.Encode("привет, мама");

        Assert.Equal([101, 3000, 1010, 2000, 102], tokens);
    }

    [Fact]
    public void A_sequence_is_cut_to_the_budget_and_keeps_both_special_tokens()
    {
        using var tokenizer = TokenizerFixture.Create();

        var tokens = tokenizer.Value.Encode("the cat sits the cat sits the cat sits", maxTokens: 5);

        Assert.Equal(5, tokens.Count);
        Assert.Equal(101, tokens[0]);
        Assert.Equal(102, tokens[^1]);
    }

    [Fact]
    public void Text_the_vocabulary_does_not_know_becomes_the_unknown_token()
    {
        using var tokenizer = TokenizerFixture.Create();

        var tokens = tokenizer.Value.Encode("привет zzzzz");

        Assert.Equal([101, 3000, 100, 102], tokens);
    }

    [Fact]
    public void Pooling_ignores_the_positions_the_mask_marks_as_padding()
    {
        // Three positions of two components: the third is padding, and its value must not be averaged in.
        var hidden = new float[] { 1, 1, 3, 3, 100, 100 };
        var mask = new long[] { 1, 1, 0 };

        var pooled = OnnxEmbeddingProvider.MeanPool(hidden, mask, tokenCount: 3, dimensions: 2);

        Assert.Equal([2f, 2f], pooled);
    }

    [Fact]
    public void A_vector_does_not_depend_on_the_padding_it_was_batched_with()
    {
        var withShortPadding = OnnxEmbeddingProvider.MeanPool(
            [1, 1, 3, 3, 7, 7],
            [1, 1, 0],
            tokenCount: 3,
            dimensions: 2);
        var withLongPadding = OnnxEmbeddingProvider.MeanPool(
            [1, 1, 3, 3, 999, 999],
            [1, 1, 0],
            tokenCount: 3,
            dimensions: 2);

        // Same text, different neighbour in the batch: the vector has to be the same one.
        Assert.Equal(withShortPadding, withLongPadding);
    }

    [Fact]
    public void A_sequence_with_no_token_pools_to_zero_rather_than_to_everything()
    {
        var pooled = OnnxEmbeddingProvider.MeanPool(
            [5, 5, 5, 5],
            [0, 0],
            tokenCount: 2,
            dimensions: 2);

        Assert.Equal([0f, 0f], pooled);
    }

    [Fact]
    public void A_vector_is_scaled_to_unit_length()
    {
        var vector = new float[] { 3, 4 };

        OnnxEmbeddingProvider.NormalizeInPlace(vector);

        Assert.Equal(0.6f, vector[0], 6);
        Assert.Equal(0.8f, vector[1], 6);
        Assert.Equal(1d, Math.Sqrt((vector[0] * vector[0]) + (vector[1] * vector[1])), 6);
    }

    [Fact]
    public void Loading_a_model_that_is_not_installed_names_the_command_that_fetches_it()
    {
        using var workspace = WorkspaceFixture.Create();

        var exception = Assert.Throws<FileNotFoundException>(() =>
            OnnxEmbeddingProvider.Load(workspace.Paths, workspace.Model, dimensions: 384));

        Assert.Contains("aiko model download", exception.Message, StringComparison.Ordinal);
        Assert.Contains(workspace.Model.Id, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tokenizer_that_is_not_WordPiece_is_refused_rather_than_read_as_one()
    {
        var path = WriteRawTokenizer("""{ "model": { "type": "BPE", "vocab": { "a": 1 } } }""");

        var exception = Assert.Throws<InvalidDataException>(() => HuggingFaceTokenizer.FromFile(path));

        Assert.Contains("BPE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_tokenizer_file_is_reported_as_not_installed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiko-absent-{Guid.NewGuid():N}", "tokenizer.json");

        Assert.Throws<FileNotFoundException>(() => HuggingFaceTokenizer.FromFile(path));
    }

    private static string WriteRawTokenizer(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aiko-raw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tokenizer.json");
        File.WriteAllText(path, json);
        return path;
    }
}
