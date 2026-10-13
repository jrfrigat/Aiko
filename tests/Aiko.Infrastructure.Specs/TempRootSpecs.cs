using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The guard on removing a fixture's temporary root. The removal itself is an allowance for a file system that
/// still holds what a CLI child just wrote, so what is worth a test is the part that can be wrong in a way
/// nobody notices: refusing a path that is not a temporary root of this suite's own.
/// </summary>
public sealed class TempRootSpecs
{
    [Fact]
    public void It_refuses_a_path_that_is_not_rooted()
    {
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete(string.Empty));
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete("   "));
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete("aiko-local-feed-not-rooted"));
    }

    [Fact]
    public void It_refuses_a_path_outside_the_temporary_directory()
    {
        // The temporary directory itself is not one of this suite's roots: a fixture that handed it over by
        // mistake would take the machine's other temporary files with it.
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete(Path.GetTempPath()));
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete(Path.Combine(Path.GetTempPath(), "..")));

        var repository = Path.GetDirectoryName(Path.GetDirectoryName(typeof(TempRootSpecs).Assembly.Location))!;
        Assert.Throws<InvalidOperationException>(() => TempRoot.Delete(Path.Combine(repository, "aiko")));
    }

    [Fact]
    public void It_removes_a_root_of_its_own_with_everything_inside_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-temp-root-spec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "aiko.db"), "a database");
        File.WriteAllText(Path.Combine(root, "project.aiko"), "a file");

        TempRoot.Delete(root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Removing_the_same_root_twice_is_harmless()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-temp-root-spec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        TempRoot.Delete(root);
        TempRoot.Delete(root);

        Assert.False(Directory.Exists(root));
    }
}
