using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The comparison a fingerprint assertion fails with. Its whole worth is the message: the same fingerprints
/// have to read as <c>nothing</c> (that is what the assertion checks), and a moved entry has to be named - the
/// entry, and whether it was added, changed or removed.
/// </summary>
public sealed class FingerprintDiffSpecs
{
    private const string Fingerprint =
        "project/.aiko/board.json|aa\n"
        + "project/.gitignore|bb\n"
        + "data|cc\n";

    [Fact]
    public void Identical_fingerprints_are_nothing()
    {
        Assert.Equal(FingerprintDiff.Nothing, FingerprintDiff.Between(Fingerprint, Fingerprint));
    }

    [Fact]
    public void The_entry_order_does_not_matter()
    {
        var reordered = "data|cc\nproject/.gitignore|bb\nproject/.aiko/board.json|aa\n";

        Assert.Equal(FingerprintDiff.Nothing, FingerprintDiff.Between(Fingerprint, reordered));
    }

    [Fact]
    public void A_changed_entry_is_named()
    {
        var after = Fingerprint.Replace("project/.aiko/board.json|aa", "project/.aiko/board.json|zz", StringComparison.Ordinal);

        Assert.Equal("changed project/.aiko/board.json", FingerprintDiff.Between(Fingerprint, after));
    }

    [Fact]
    public void An_added_and_a_removed_entry_are_named()
    {
        var after = Fingerprint.Replace("data|cc\n", "data|cc\ndata/logs/aiko.log|dd\n", StringComparison.Ordinal)
            .Replace("project/.gitignore|bb\n", string.Empty, StringComparison.Ordinal);

        var difference = FingerprintDiff.Between(Fingerprint, after);

        Assert.Contains("added data/logs/aiko.log", difference, StringComparison.Ordinal);
        Assert.Contains("removed project/.gitignore", difference, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fingerprint_that_watches_nothing_is_not_equal_to_one_that_watches_something()
    {
        // The trap this guards: a comparison over an empty fingerprint would say "nothing" to everything,
        // which is how a fingerprint that named no file would pass over a run that rewrote one.
        Assert.NotEqual(FingerprintDiff.Nothing, FingerprintDiff.Between(string.Empty, Fingerprint));
    }
}
