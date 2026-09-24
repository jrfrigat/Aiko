using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// `aiko repair --fix` puts back what is already there; it does not connect anything new. It used to apply
/// every agent found on the machine to every registered project and to the user scope - so fixing a port wrote
/// AGENTS.md, .codex and .cursor into every repository and re-created a user-scope connection someone had just
/// removed.
/// </summary>
public sealed class CliRepairSpecs
{
    [Fact]
    public async Task A_repair_refreshes_the_connections_there_are_and_makes_no_new_ones()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-cli-repair-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "data", "aiko.db");
        var home = Path.Combine(root, "home");
        var connected = Path.Combine(root, "connected");
        var untouched = Path.Combine(root, "untouched");
        try
        {
            // Two agents look installed on this machine; one project is connected to one of them, the other to
            // none, and nothing is connected at the user scope.
            Directory.CreateDirectory(Path.Combine(home, ".claude"));
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            Directory.CreateDirectory(connected);
            Directory.CreateDirectory(untouched);
            await new DaemonEndpointConfiguration(new AikoDataPaths(databasePath))
                .LoadOrCreateAsync(24_571, CancellationToken.None);

            var (initialized, initOutput) = await CliInitSpecs.RunCliAsync(
                databasePath, home, "init", connected, "--agent", "claude-code");
            Assert.True(initialized == 0, initOutput);
            (initialized, initOutput) = await CliInitSpecs.RunCliAsync(databasePath, home, "init", untouched);
            Assert.True(initialized == 0, initOutput);

            var connectedBefore = Files(connected);
            var untouchedBefore = Files(untouched);
            var homeBefore = Files(home);

            var (exitCode, output) = await CliInitSpecs.RunCliAsync(databasePath, home, "repair", "--fix");

            Assert.True(exitCode == 0, output);
            // The connection that exists is refreshed - that is what the repair is for - and nothing else is.
            Assert.Contains("claude-code repaired", output, StringComparison.Ordinal);
            Assert.DoesNotContain("codex repaired", output, StringComparison.Ordinal);
            Assert.DoesNotContain("user scope:", output, StringComparison.Ordinal);
            Assert.Equal(untouchedBefore, Files(untouched));
            Assert.Equal(connectedBefore, Files(connected));
            Assert.Equal(homeBefore, Files(home));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>The files under a directory, outside Aiko's own data, as sorted relative paths.</summary>
    private static string[] Files(string directory) =>
        [.. Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith(".aiko/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
}
