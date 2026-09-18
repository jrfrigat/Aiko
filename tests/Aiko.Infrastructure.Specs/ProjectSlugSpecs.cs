using Aiko.Application.Contracts;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The readable project handle: how a name becomes one, what counts as valid, and what happens on a clash.
/// </summary>
public sealed class ProjectSlugSpecs
{
    [Fact]
    public void A_name_is_transliterated_into_a_readable_handle()
    {
        // The requirement's own example: the name keeps its spelling, the handle is latin.
        Assert.Equal("moj-pervyj-proekt", ProjectSlug.Derive("Мой пеРвыЙ проект"));
        Assert.Equal("aiko", ProjectSlug.Derive("Aiko"));
        Assert.Equal("aiko", ProjectSlug.Derive("  Aiko  "));
    }

    [Fact]
    public void Punctuation_and_repeated_separators_collapse_into_one_dash()
    {
        Assert.Equal("hello-world", ProjectSlug.Derive("Hello, world!"));
        Assert.Equal("a-b", ProjectSlug.Derive("a  ---  b"));
        Assert.Equal("hello-world", ProjectSlug.Derive(" - Hello,  world! - "));
    }

    [Fact]
    public void A_name_with_nothing_usable_becomes_the_fallback()
    {
        Assert.Equal(ProjectSlug.Fallback, ProjectSlug.Derive("!!!"));
        Assert.Equal(ProjectSlug.Fallback, ProjectSlug.Derive("   "));
        Assert.Equal(ProjectSlug.Fallback, ProjectSlug.Derive(null));
    }

    [Fact]
    public void A_long_name_is_trimmed_to_a_url_sized_handle()
    {
        Assert.Equal(ProjectSlug.MaxLength, ProjectSlug.Derive(new string('a', 200)).Length);
    }

    [Fact]
    public void Only_lower_case_latin_letters_digits_and_dashes_are_valid()
    {
        Assert.True(ProjectSlug.IsValid("aiko"));
        Assert.True(ProjectSlug.IsValid("my-project-2"));
        Assert.False(ProjectSlug.IsValid(null));
        Assert.False(ProjectSlug.IsValid(string.Empty));
        Assert.False(ProjectSlug.IsValid("Aiko"));
        Assert.False(ProjectSlug.IsValid("aiko proj"));
        Assert.False(ProjectSlug.IsValid("aiko_proj"));
        Assert.False(ProjectSlug.IsValid("-aiko"));
        Assert.False(ProjectSlug.IsValid("aiko-"));
    }

    [Fact]
    public void A_taken_handle_gets_a_numeric_suffix()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "aiko", "aiko-2" };
        Assert.Equal("aiko-3", ProjectSlug.MakeUnique("aiko", taken.Contains));
        Assert.Equal("aiko-2", ProjectSlug.MakeUnique("aiko", slug => slug == "aiko"));
        // Even a name that carries nothing a slug can keep stays usable.
        Assert.Equal(ProjectSlug.Fallback, ProjectSlug.MakeUnique("!!!", _ => false));
    }
}
