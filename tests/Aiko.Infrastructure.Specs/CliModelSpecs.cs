using Aiko.Infrastructure.Models;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// `aiko model` from the terminal, with no network and no hundred-megabyte file.
/// </summary>
/// <remarks>
/// What a spec can prove here is the command surface: what each command says, what it refuses, and that a
/// refused fetch writes nothing. That a downloaded file verifies - and that both delivery paths are held to the
/// same rule - is proven in <c>EmbeddingModelSpecs</c> against a synthetic model, which is the only way to test
/// it without downloading the real thing.
/// </remarks>
public sealed class CliModelSpecs
{
    [Fact]
    public async Task The_model_commands_say_what_they_know_and_write_nothing_they_cannot_verify()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-cli-model-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "data", "aiko.db");
        var userHome = Path.Combine(root, "home");

        // An empty release mirror: the release path is wired and reachable, and has nothing to serve. That is
        // the shape of the world until the model's assets are published, and the command has to explain it.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AIKO_RELEASE_FEED"] = Path.Combine(root, "feed")
        };

        var modelDirectory = Path.Combine(
            Path.GetDirectoryName(databasePath)!,
            "models",
            BuiltInEmbeddingModelCatalog.MultilingualMiniLmId);

        try
        {
            Directory.CreateDirectory(root);

            var (listed, listOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "list"]);
            Assert.True(listed == 0, listOutput);
            Assert.Contains(BuiltInEmbeddingModelCatalog.MultilingualMiniLmId, listOutput, StringComparison.Ordinal);
            Assert.Contains("apache-2.0", listOutput, StringComparison.Ordinal);
            Assert.Contains("not installed", listOutput, StringComparison.Ordinal);

            var (status, statusOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "status"]);
            Assert.True(status == 0, statusOutput);
            // The empty case is said in words, and the line names what is missing.
            Assert.Contains("Not installed", statusOutput, StringComparison.Ordinal);
            Assert.Contains("not present", statusOutput, StringComparison.Ordinal);

            // A channel that does not carry the model is explained, not crashed on - and nothing is written.
            var (refused, refusedOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "download", "--source", "release"]);
            Assert.True(refused == 1, refusedOutput);
            Assert.Contains("Could not fetch the model", refusedOutput, StringComparison.Ordinal);
            Assert.Contains("--source external", refusedOutput, StringComparison.Ordinal);
            Assert.False(Directory.Exists(modelDirectory), "a refused download left something behind.");

            var (removed, removedOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "remove"]);
            Assert.True(removed == 0, removedOutput);
            Assert.Contains("nothing to remove", removedOutput, StringComparison.OrdinalIgnoreCase);

            // A wrong source and a wrong model are refused before anything is fetched, and both name the way out.
            var (badSource, badSourceOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "download", "--source", "floppy"]);
            Assert.True(badSource == 2, badSourceOutput);
            Assert.Contains("release or external", badSourceOutput, StringComparison.Ordinal);

            var (badModel, badModelOutput) = await CliInitSpecs.RunCliWithEnvironmentAsync(
                databasePath, userHome, environment, ["model", "status", "no-such-model"]);
            Assert.True(badModel == 2, badModelOutput);
            Assert.Contains("no-such-model", badModelOutput, StringComparison.Ordinal);
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
