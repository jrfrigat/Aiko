using Aiko.Application.Contracts;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the rule that decides whether a live event concerns the project that is open on screen.
/// </summary>
public sealed class ProjectEventScopeSpecs
{
    private static readonly RegisteredProject Project = new(
        "01a0aec7627476cb9e622df271b9b20f",
        "Aiko",
        @"C:\Projects\Aiko",
        "aiko");

    [Fact]
    public void An_event_is_matched_by_the_project_id_and_not_by_the_handle_in_the_url()
    {
        // The daemon journals and broadcasts under the immutable id...
        Assert.True(WorkspaceState.BelongsToOpenProject(Project.Id, Project));

        // ...while the URL that opened the project carries the readable handle. Comparing those two strings
        // is what silently dropped every event of a project addressed by its handle - the URL the UI hands
        // out - and left the board and the card page stale until the page was reloaded by hand.
        Assert.False(WorkspaceState.BelongsToOpenProject(Project.Handle, Project));
    }

    [Fact]
    public void An_event_of_another_project_is_not_for_this_screen()
    {
        var other = Project with { Id = "01a0b037000000000000000000000000", Slug = "other" };
        Assert.False(WorkspaceState.BelongsToOpenProject(other.Id, Project));

        // The dashboard has no open project, so no project event belongs to it.
        Assert.False(WorkspaceState.BelongsToOpenProject(Project.Id, null));
    }

    [Fact]
    public void A_project_whose_handle_is_its_id_still_matches()
    {
        // A registration written before slugs existed is addressed by its id, and then the two are one.
        var legacy = Project with { Slug = null };
        Assert.Equal(legacy.Id, legacy.Handle);
        Assert.True(WorkspaceState.BelongsToOpenProject(legacy.Id, legacy));
    }
}
