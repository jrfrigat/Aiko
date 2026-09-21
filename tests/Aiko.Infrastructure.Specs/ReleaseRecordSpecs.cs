using Aiko.Application.Contracts;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The rules a release record obeys: what it must carry, and when it may not be written at all.
/// </summary>
/// <remarks>
/// These are the refusals the acceptance asks for ("the refusal rules are checked"), and they are checked
/// without a project or a file: a rule that needs a database to be exercised is a rule nobody can read.
/// </remarks>
public sealed class ReleaseRecordSpecs
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_release_without_a_version_is_refused(string? version) =>
        Assert.Throws<ArgumentException>(() => ReleaseRecords.Validate(version, ReleaseSchemes.GitReleaseId));

    [Theory]
    [InlineData("0.1.3")]
    [InlineData("1.0")]
    [InlineData("release-2")]
    public void A_version_that_is_not_a_tag_is_refused(string version) =>
        Assert.Throws<ArgumentException>(() => ReleaseRecords.Validate(version, ReleaseSchemes.GitReleaseId));

    [Fact]
    public void A_release_without_a_scheme_is_refused() =>
        Assert.Throws<ArgumentException>(() => ReleaseRecords.Validate("v0.1.3", "   "));

    [Fact]
    public void A_tag_shaped_version_with_a_scheme_is_accepted()
    {
        ReleaseRecords.Validate("v0.1.3", ReleaseSchemes.GitReleaseId);
        ReleaseRecords.Validate("v0.1.3-pre", ReleaseSchemes.GitPreReleaseId);
    }

    [Fact]
    public void A_second_record_of_one_version_is_refused()
    {
        var existing = new[] { Release("v0.1.3") };

        var refusal = ReleaseRecords.RefuseRecord("v0.1.3", existing);

        Assert.NotNull(refusal);
        // The refusal names the release that is already there, so whoever reads it knows what happened.
        Assert.Contains("v0.1.3", refusal, StringComparison.Ordinal);
        Assert.Null(ReleaseRecords.RefuseRecord("v0.1.4", existing));
    }

    [Fact]
    public void One_version_is_one_version_whatever_its_case_and_spacing()
    {
        // A history with two records for one tag answers "which cards went into v0.1.3" twice, which is the
        // one thing the explicit list exists to prevent.
        var existing = new[] { Release("v0.1.3") };

        Assert.NotNull(ReleaseRecords.RefuseRecord(" V0.1.3 ", existing));
    }

    [Fact]
    public void The_card_list_keeps_what_was_named_and_loses_the_blanks()
    {
        Assert.Equal(
            new[] { "TASK-1", "TASK-2" },
            ReleaseRecords.NormalizeCards(["TASK-1", "  ", " TASK-2 "]));
        Assert.Empty(ReleaseRecords.NormalizeCards(null));
        // An empty list is a statement, not a hole: a release that carried no card says exactly that.
        Assert.Empty(ReleaseRecords.NormalizeCards([]));
    }

    private static ReleaseRecord Release(string version) =>
        new(version, ReleaseSchemes.GitReleaseId, DateTimeOffset.UtcNow, null, []);
}
