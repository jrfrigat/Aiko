using Aiko.Application.Contracts;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The release schemes a project holds: the two Aiko ships, and the ones a project writes for itself.
/// </summary>
/// <remarks>
/// The acceptance puts two things together - "the schemes ship with the installation" and "a project keeps its
/// own, and is not limited to one" - and those are one mechanism: a level that states no release section
/// carries both shipped schemes, and one that writes its own carries those instead. Which scheme a release
/// follows is named where the release is asked for, so nothing here chooses one; these specs hold that, and
/// hold that an empty list is silence rather than an empty catalogue.
/// </remarks>
public sealed class ReleaseSchemeSpecs
{
    [Fact]
    public void Aiko_ships_two_schemes_that_say_different_things()
    {
        Assert.Equal(2, ReleaseSchemes.BuiltIn.Count);

        var ordinary = ReleaseSchemes.GitRelease();
        var preliminary = ReleaseSchemes.GitPreRelease();

        Assert.Equal(ReleaseSchemes.GitReleaseId, ordinary.Id);
        Assert.Equal(ReleaseSchemes.GitPreReleaseId, preliminary.Id);
        Assert.NotEqual(ordinary.Id, preliminary.Id);

        // A scheme with no body is a scheme whose steps nobody can follow.
        foreach (var scheme in ReleaseSchemes.BuiltIn)
        {
            Assert.False(string.IsNullOrWhiteSpace(scheme.Name));
            Assert.False(string.IsNullOrWhiteSpace(scheme.Description));
            Assert.False(string.IsNullOrWhiteSpace(scheme.Body));
        }

        Assert.NotEqual(ordinary.Body, preliminary.Body);
    }

    [Fact]
    public void Only_the_preliminary_scheme_names_the_pre_release_suffix()
    {
        // The suffix is the whole difference between the two, so it must be where the scheme says so - and
        // the ordinary scheme must not mention a suffix it never sets.
        Assert.Contains(
            ReleaseSchemes.PreReleaseSuffix,
            ReleaseSchemes.GitPreRelease().Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ReleaseSchemes.PreReleaseSuffix,
            ReleaseSchemes.GitRelease().Body,
            StringComparison.Ordinal);

        // Both still tag by the same rule, which is what the brief asked for.
        Assert.Contains("v<major>", ReleaseSchemes.GitRelease().Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_level_that_states_nothing_holds_both_shipped_schemes()
    {
        var settings = ReleaseSettings.SafeDefault;

        Assert.Equal(2, settings.EffectiveSchemes.Count);
        Assert.Equal(ReleaseSchemes.GitReleaseId, settings.EffectiveSchemes[0].Id);
        Assert.Equal(ReleaseSchemes.GitPreReleaseId, settings.EffectiveSchemes[1].Id);

        // Silence is a working configuration, not a fallback the screen has to apologize for.
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void A_project_holds_as_many_schemes_of_its_own_as_it_needs()
    {
        // The point of a list rather than a choice: a project writes the orders it releases by, and no one of
        // them is "in force" - the release itself names the scheme it follows.
        var own = new[]
        {
            new ReleaseScheme("team-release", "Team release", "Our own order.", "1. Ask the release owner."),
            new ReleaseScheme("hotfix", "Hotfix", "A fix out of band.", "1. Tag the fix and publish it."),
        };
        var settings = new ReleaseSettings(Schemes: own);

        Assert.Equal(2, settings.EffectiveSchemes.Count);
        Assert.Equal("team-release", settings.EffectiveSchemes[0].Id);
        Assert.Equal("Our own order.", settings.EffectiveSchemes[0].Description);
        Assert.Equal("hotfix", settings.EffectiveSchemes[1].Id);
    }

    [Fact]
    public void An_empty_list_is_silence_and_not_an_empty_catalogue()
    {
        // "Schemes: []" says nothing rather than offering nothing: a level whose list is empty still has the
        // two Aiko ships, because an empty list of steps is not a configuration anyone can release with.
        var settings = new ReleaseSettings(Schemes: []);

        Assert.Equal(2, settings.EffectiveSchemes.Count);
        Assert.Equal(ReleaseSchemes.GitReleaseId, settings.EffectiveSchemes[0].Id);
    }

    [Fact]
    public void A_section_written_before_schemes_existed_still_releases()
    {
        // This is what a file written before any of this looks like: owner and repository, no list. It still
        // resolves to a catalogue a release can be conducted by.
        var settings = new ReleaseSettings("jrfrigat", "Aiko");

        Assert.Equal(2, settings.EffectiveSchemes.Count);
        Assert.True(settings.IsConfigured);
        Assert.Equal("jrfrigat/Aiko", settings.Slug);
    }
}
