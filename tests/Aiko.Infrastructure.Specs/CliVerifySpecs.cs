using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// `aiko verify` from the terminal: the same check the MCP tool returns, plus the <c>--seal</c> that is the only
/// way a baseline is recorded. The exit code matters here, because a script asks the question - and so does the
/// fact that a person who edits a file by hand is told what they changed rather than that their project rotted.
/// </summary>
public sealed class CliVerifySpecs
{
    [Fact]
    public async Task A_verify_changes_nothing_and_a_seal_is_what_makes_the_next_check_pass()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-cli-verify-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "data", "aiko.db");
        var userHome = Path.Combine(root, "home");
        var projectRoot = Path.Combine(root, "project");
        var manifest = Path.Combine(projectRoot, ".aiko", "manifest.json");
        try
        {
            Directory.CreateDirectory(projectRoot);
            var (initialized, initOutput) = await CliInitSpecs.RunCliAsync(
                databasePath,
                userHome,
                "init",
                projectRoot,
                "--id",
                "verify-test");
            Assert.True(initialized == 0, initOutput);

            // No baseline yet: the check says so and exits 1, because "not verified" is not "intact".
            var (noBaseline, noBaselineOutput) = await CliInitSpecs.RunCliAsync(databasePath, userHome, "verify");
            Assert.True(noBaseline == 1, noBaselineOutput);
            Assert.Contains("No baseline yet", noBaselineOutput, StringComparison.Ordinal);
            Assert.False(File.Exists(manifest), "a check wrote a baseline of its own.");

            var (sealedExit, sealedOutput) = await CliInitSpecs.RunCliAsync(
                databasePath,
                userHome,
                "verify",
                "--project",
                "verify-test",
                "--seal");
            Assert.True(sealedExit == 0, sealedOutput);
            Assert.True(File.Exists(manifest), sealedOutput);
            Assert.Contains("(verify-test)", sealedOutput, StringComparison.Ordinal);

            var sealedBytes = await File.ReadAllBytesAsync(manifest);
            var (clean, cleanOutput) = await CliInitSpecs.RunCliAsync(databasePath, userHome, "verify");
            Assert.True(clean == 0, cleanOutput);
            Assert.Contains("Everything looks intact.", cleanOutput, StringComparison.Ordinal);
            Assert.Equal(sealedBytes, await File.ReadAllBytesAsync(manifest));

            // A person adds a memory file by hand, which is the point of keeping the project's data in files.
            // A file nobody has seen before is not damage: the check names it and still exits 0.
            var added = Path.Combine(projectRoot, ".aiko", "memory", "conventions", "edited.md");
            Directory.CreateDirectory(Path.GetDirectoryName(added)!);
            await File.WriteAllTextAsync(added, "# Edited by hand\n");

            var (withAddition, withAdditionOutput) = await CliInitSpecs.RunCliAsync(
                databasePath,
                userHome,
                "verify");
            Assert.True(withAddition == 0, withAdditionOutput);
            Assert.Contains(
                "memory/conventions/edited.md - Added since the baseline.",
                withAdditionOutput,
                StringComparison.Ordinal);

            // Editing a file the baseline knows is the case that has to be visible: the check says which file
            // differs from it and exits 1, so a script can act on the answer.
            var named = Path.Combine(projectRoot, ".aiko", "memory", "index.md");
            await File.WriteAllTextAsync(named, "# Index\n\nEdited by hand.\n");

            var (drifted, driftedOutput) = await CliInitSpecs.RunCliAsync(databasePath, userHome, "verify");
            Assert.True(drifted == 1, driftedOutput);
            Assert.Contains(
                "memory/index.md - Changed since the baseline.",
                driftedOutput,
                StringComparison.Ordinal);

            // Accepting both edits is sealing again, and then the project is intact by its own definition.
            var (resealed, resealedOutput) = await CliInitSpecs.RunCliAsync(
                databasePath,
                userHome,
                "verify",
                "--seal");
            Assert.True(resealed == 0, resealedOutput);

            var (afterSeal, afterSealOutput) = await CliInitSpecs.RunCliAsync(databasePath, userHome, "verify");
            Assert.True(afterSeal == 0, afterSealOutput);
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
}
