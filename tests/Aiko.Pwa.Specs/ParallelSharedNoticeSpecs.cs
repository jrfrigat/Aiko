using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards that the board no longer carries the parallel-shared-mode warning.
/// </summary>
/// <remarks>
/// The owner asked for it to go: it took space on the board without being actionable there. The mode itself is
/// still configured in the project settings, so removing the notice changes what the board says, not what the
/// product supports.
/// </remarks>
public sealed class ParallelSharedNoticeSpecs
{
    [Fact]
    public void The_board_carries_no_parallel_shared_warning()
    {
        Assert.DoesNotContain(
            "ParallelShared",
            Text("src", "Aiko.Pwa", "Pages", "BoardView.razor"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_helper_the_warning_used_is_gone()
    {
        // The board was its only reader, so leaving the method behind would be dead code pretending to be API.
        Assert.DoesNotContain(
            "IsParallelShared",
            Text("src", "Aiko.Pwa", "Services", "BoardMetrics.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_warning_text_is_no_longer_shipped()
    {
        foreach (var file in new[] { "Loc.resx", "Loc.ru.resx" })
        {
            var text = Text("src", "Aiko.Pwa", "Resources", file);
            Assert.DoesNotContain("ParallelSharedTitle", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ParallelSharedText", text, StringComparison.Ordinal);
        }
    }

    private static string Text(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ParallelSharedNoticeSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
