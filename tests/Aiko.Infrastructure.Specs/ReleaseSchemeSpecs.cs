using Aiko.Application.Contracts;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The release schemes a project chooses from: the two Aiko ships, and what happens when a project's choice
/// names nothing.
/// </summary>
/// <remarks>
/// The acceptance puts two things together - "the schemes ship with the installation" and "a scheme can be
/// chosen for the project and for the template" - and those are one mechanism: a project that states no
/// release section follows the safe default, which carries both schemes. These specs hold that, and hold the
/// refusal to answer a bad choice with nothing.
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
    public void A_project_that_states_nothing_gets_both_schemes_and_the_ordinary_one()
    {
        var settings = ReleaseSettings.SafeDefault;

        Assert.Equal(2, settings.AvailableSchemes.Count);
        Assert.Equal(ReleaseSchemes.GitReleaseId, settings.ResolveScheme().Id);

        // Silence is a working configuration, not a fallback the screen has to apologize for.
        Assert.False(settings.SchemeFellBack);
    }

    [Fact]
    public void The_project_s_own_choice_wins()
    {
        var settings = ReleaseSettings.SafeDefault with { Scheme = ReleaseSchemes.GitPreReleaseId };

        Assert.Equal(ReleaseSchemes.GitPreReleaseId, settings.ResolveScheme().Id);
        Assert.False(settings.SchemeFellBack);
    }

    [Fact]
    public void A_choice_that_names_nothing_resolves_rather_than_throwing()
    {
        // A scheme removed from the list leaves a settings file that still names it. The answer is the
        // ordinary release plus the fact that the choice was not honoured - never an exception and never a
        // null a screen would draw as an empty place.
        var settings = ReleaseSettings.SafeDefault with { Scheme = "a-scheme-that-is-gone" };

        Assert.Equal(ReleaseSchemes.GitReleaseId, settings.ResolveScheme().Id);
        Assert.True(settings.SchemeFellBack);
    }

    [Fact]
    public void A_section_that_states_a_repository_but_no_scheme_follows_the_ordinary_one()
    {
        // This is what a settings file written before schemes existed looks like: owner and repository, no
        // choice. It resolves, and the screen is told the choice is not its own.
        var settings = new ReleaseSettings("jrfrigat", "Aiko");

        Assert.Equal(2, settings.AvailableSchemes.Count);
        Assert.Equal(ReleaseSchemes.GitReleaseId, settings.ResolveScheme().Id);
        Assert.True(settings.SchemeFellBack);
    }

    [Fact]
    public void A_project_may_state_its_own_schemes_and_one_of_them_wins()
    {
        // The point of the list being a list: a project defines a scheme of its own and follows it.
        var own = new ReleaseScheme("team-release", "Team release", "Our own order.", "1. Ask the release owner.");
        var settings = new ReleaseSettings(Scheme: "team-release", Schemes: [own]);

        Assert.Single(settings.AvailableSchemes);
        Assert.Equal("team-release", settings.ResolveScheme().Id);
        Assert.Equal("Our own order.", settings.ResolveScheme().Description);
        Assert.False(settings.SchemeFellBack);
    }

    [Fact]
    public void An_empty_list_is_silence_and_not_an_empty_catalogue()
    {
        // "Schemes: []" says nothing rather than offering nothing: a project whose list is empty still has
        // the two Aiko ships, because an empty list of steps is not a configuration anyone can release with.
        var settings = new ReleaseSettings(Scheme: ReleaseSchemes.GitPreReleaseId, Schemes: []);

        Assert.Equal(2, settings.AvailableSchemes.Count);
        Assert.Equal(ReleaseSchemes.GitPreReleaseId, settings.ResolveScheme().Id);
    }
}
