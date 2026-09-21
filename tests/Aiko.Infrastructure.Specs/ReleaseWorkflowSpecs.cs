using Aiko.Application.Contracts;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the one thing the release workflow has to decide about a tag: whether the release it publishes is
/// the current one.
/// </summary>
/// <remarks>
/// A release that carries a pre-release part in its tag is not the current release, and GitHub does not infer
/// that from the tag name - while <c>scripts/install.ps1</c> resolves "latest release". Left as the latest
/// one, a preliminary release would be installed by everyone, which is the opposite of what the
/// <c>git-pre-release</c> scheme is for.
/// <para>
/// This spec reads the workflow file rather than running it: the file is GitHub Actions, and no suite here can
/// execute it. So it holds the file's <em>intention</em> - that the rule is stated and that the ordinary path
/// did not lose its own - and not the behaviour on a runner. That behaviour is confirmed by the first real
/// pre-release, which is a person's step, not a suite's.
/// </para>
/// </remarks>
public sealed class ReleaseWorkflowSpecs
{
    [Fact]
    public void A_tag_with_a_pre_release_part_is_published_as_a_pre_release()
    {
        var finalize = Finalize();

        // The decision is taken from the tag the job already holds - no new input and no label, because the
        // tag is the only source of the version and the person writes it anyway.
        Assert.Contains("case \"$TAG\" in", finalize, StringComparison.Ordinal);
        Assert.Contains("*-*)", finalize, StringComparison.Ordinal);
        Assert.Contains("--prerelease", finalize, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pre_release_is_taken_off_the_latest_pointer_explicitly()
    {
        var finalize = Finalize();

        // "not passing --latest" is not the same as "not latest": a re-run of this job may find the release
        // already marked latest, and only stating it clears that.
        Assert.Contains("--latest=false", finalize, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_tag_becomes_the_latest_release_as_before()
    {
        var finalize = Finalize();

        // The ordinary path keeps what it had, or this change would have broken every normal release.
        Assert.Contains("--draft=false --latest)", finalize, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_paths_publish_the_release_once_its_archive_is_attached()
    {
        var finalize = Finalize();

        // The order the workflow is built around - invisible until the archive is on it - is untouched in
        // both branches: the difference between them is only which kind of release it is.
        Assert.Equal(2, Count(finalize, "--draft=false"));
        Assert.Contains("gh release edit \"$TAG\" -R \"$REPO\" \"${flags[@]}\"", finalize, StringComparison.Ordinal);
    }

    [Fact]
    public void The_suffix_the_scheme_writes_is_a_pre_release_part_of_the_tag()
    {
        // The workflow's rule is "a pre-release part is present", which semver spells with a leading hyphen.
        // The scheme's suffix has to be one for the two to agree - so this is the one place that ties the
        // scheme to the workflow rather than to a comment.
        Assert.StartsWith("-", ReleaseSchemes.PreReleaseSuffix, StringComparison.Ordinal);
        Assert.Contains(
            ReleaseSchemes.PreReleaseSuffix,
            $"v1.2.3{ReleaseSchemes.PreReleaseSuffix}",
            StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_else_about_the_release_path_changed()
    {
        var workflow = Workflow();

        // The parts this task promised not to touch, checked so that a later edit has to be deliberate:
        // the trigger, the version taken from the tag, the draft, and the archive the installer looks for.
        Assert.Contains("tags: [ 'v*' ]", workflow, StringComparison.Ordinal);
        Assert.Contains("value=${TAG#v}", workflow, StringComparison.Ordinal);
        Assert.Contains("--draft", workflow, StringComparison.Ordinal);
        Assert.Contains("\"aiko-\"*\"-win-x64.zip\"", workflow, StringComparison.Ordinal);
    }

    /// <summary>The finalize job, which is the last one in the file.</summary>
    private static string Finalize()
    {
        var workflow = Workflow();
        var start = workflow.IndexOf("  finalize:", StringComparison.Ordinal);
        Assert.True(start >= 0, "The release workflow should still have the 'finalize' job.");
        return workflow[start..];
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "release.yml"));

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ReleaseWorkflowSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
