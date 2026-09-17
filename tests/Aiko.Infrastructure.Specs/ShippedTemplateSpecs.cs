using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the base template the installer ships.
/// </summary>
/// <remarks>
/// Two things write the base template: the install script copies <c>assets/templates/default/template.json</c>
/// into the daemon's data directory, and the daemon writes the same document itself when nothing is shipped
/// (a source build). If those drift, an installation and a working copy would start new projects from
/// different defaults, and nothing else would notice - so the shipped file is pinned to the built-in one
/// here. Regenerate it by taking the file the daemon writes for an empty templates root.
/// </remarks>
public sealed class ShippedTemplateSpecs
{
    [Fact]
    public async Task The_shipped_base_template_is_the_one_the_daemon_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AikoDataPaths(Path.Combine(root, "data", "aiko.db"));
            var store = new FileProjectTemplateStore(paths);
            var written = await store.EnsureDefaultAsync(CancellationToken.None);

            var writtenPath = Path.Combine(paths.TemplateDirectory(written.Id), "template.json");
            var shippedPath = Path.Combine(
                FindRepositoryRoot(),
                "assets",
                "templates",
                ProjectTemplate.DefaultId,
                "template.json");

            Assert.True(
                File.Exists(shippedPath),
                $"The installer's base template is missing: {shippedPath}");

            // Line endings are not the contract; the document is.
            static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(
                Normalize(await File.ReadAllTextAsync(writtenPath)),
                Normalize(await File.ReadAllTextAsync(shippedPath)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ShippedTemplateSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
