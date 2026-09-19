using System.Text.RegularExpressions;
using Aiko.Application.Contracts;
using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the addresses the cockpit builds for its own pages.
/// </summary>
/// <remarks>
/// The defect these pin: the related-cards graph assembled its links from the immutable project id a card's
/// relation stores, so a link copied out of the UI read <c>/p/01a0aec7…/cards/TASK-15</c> where it should
/// have read <c>/p/aiko/cards/TASK-15</c>. Every address is built in one place now, and an id-addressed
/// route is answered by the handle that replaces it.
/// </remarks>
public sealed class ProjectRoutesSpecs
{
    private static readonly RegisteredProject Project =
        new("01a0aec7627476cb9e622df271b9b20f", "Aiko", "C:\\projects\\aiko", "aiko");

    [Fact]
    public void Every_page_address_names_the_project_by_its_handle()
    {
        Assert.Equal("/p/aiko", ProjectRoutes.Overview("aiko"));
        Assert.Equal("/p/aiko/board", ProjectRoutes.Board("aiko"));
        Assert.Equal("/p/aiko/backlog", ProjectRoutes.Section("aiko", "backlog"));
        Assert.Equal("/p/aiko/cards/TASK-15", ProjectRoutes.Card("aiko", "TASK-15"));
        Assert.Equal("/p/aiko/cards/new", ProjectRoutes.NewCard("aiko"));
    }

    [Fact]
    public void A_segment_is_escaped_so_it_cannot_end_the_address_early()
    {
        Assert.Equal("/p/a%20b/board", ProjectRoutes.Board("a b"));
        Assert.Equal("/p/aiko/cards/TASK%2F15", ProjectRoutes.Card("aiko", "TASK/15"));
    }

    [Fact]
    public void An_id_addressed_route_resolves_to_the_handle_that_replaces_it()
    {
        // The id resolves - it is what card files and relations store - but the answer is the handle.
        Assert.Equal("aiko", ProjectRoutes.ReadableHandle([Project], Project.Id));
        // A route that already speaks the handle has nothing to replace ...
        Assert.Null(ProjectRoutes.ReadableHandle([Project], "aiko"));
        // ... and neither has a project nobody registered, or an empty route value.
        Assert.Null(ProjectRoutes.ReadableHandle([Project], "nobody"));
        Assert.Null(ProjectRoutes.ReadableHandle([Project], string.Empty));
    }

    [Fact]
    public void The_id_addressed_address_becomes_the_handle_one_and_keeps_the_rest()
    {
        var id = Project.Id;
        const string host = "http://127.0.0.1:24598";

        Assert.Equal(
            $"{host}/p/aiko/board",
            ProjectRoutes.WithHandle($"{host}/p/{id}/board", id, "aiko"));

        // A deeper link keeps its page, its card and its query: a redirect must not lose the target.
        Assert.Equal(
            $"{host}/p/aiko/cards/TASK-15?tab=runs",
            ProjectRoutes.WithHandle($"{host}/p/{id}/cards/TASK-15?tab=runs", id, "aiko"));

        // A value that only starts like the segment is not the project segment, and a route without one is
        // left alone rather than rewritten into something nobody asked for.
        Assert.Null(ProjectRoutes.WithHandle($"{host}/p/{id}-other/board", id, "aiko"));
        Assert.Null(ProjectRoutes.WithHandle($"{host}/daemon", id, "aiko"));
    }

    [Fact]
    public void No_page_assembles_a_project_address_by_hand()
    {
        var offenders = new List<string>();
        foreach (var file in RazorPages())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                // A route declaration names the parameter; it does not build an address a person follows.
                if (line.TrimStart().StartsWith("@page", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains("\"/p/{", StringComparison.Ordinal))
                {
                    offenders.Add($"{file}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Project addresses belong to ProjectRoutes; these are assembled by hand instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void No_page_builds_an_address_from_a_card_reference()
    {
        // A relation stores the immutable project id, so an address built from CardReference would put a
        // GUID in the address bar. The helper is not a loophole either: it takes a handle.
        var offenders = new List<string>();
        foreach (var file in RazorPages())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"ProjectRoutes\.[A-Za-z]+\([^)]*Reference\.ProjectId"))
            {
                offenders.Add($"{file}: {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "An address built from a card's project reference names the project by its id:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The cockpit's screens, without the build output beside them.</summary>
    private static IEnumerable<string> RazorPages() =>
        Directory
            .EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa", "Pages"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the other specs do.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ProjectRoutesSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
