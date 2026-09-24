using System.Diagnostics;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// `aiko init` run the way a person runs it: the built CLI against a data directory of its own. The template
/// a project is created from states its git policy, and an init that names no policy takes that one - it used
/// to read the missing option as local-only, so a template that tracks project knowledge was overridden by
/// the very command that created the project from it.
/// </summary>
public sealed class CliInitSpecs
{
    [Fact]
    public async Task An_init_without_a_git_policy_takes_the_templates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiko-cli-init-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "data", "aiko.db");
        var projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        try
        {
            var templates = new FileProjectTemplateStore(new AikoDataPaths(databasePath));
            var standard = await templates.EnsureDefaultAsync(CancellationToken.None);
            await templates.WriteAsync(
                standard with { Id = "tracked", Name = "Tracked", GitPolicy = ProjectGitPolicy.TrackProjectKnowledge },
                CancellationToken.None);

            var (exitCode, output) = await RunCliAsync(
                databasePath,
                Path.Combine(root, "home"),
                "init", projectRoot, "--template", "tracked");

            Assert.True(exitCode == 0, output);
            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(projectRoot, ".aiko", "project.json")));
            var policy = manifest.RootElement.EnumerateObject()
                .Single(property => property.NameEquals("gitPolicy") || property.NameEquals("GitPolicy"))
                .Value.GetString();
            Assert.Equal(nameof(ProjectGitPolicy.TrackProjectKnowledge), policy, ignoreCase: true);
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

    [Fact]
    public void The_help_offers_no_option_the_commands_refuse()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Aiko.Cli", "Program.cs"));
        var help = program[program.IndexOf("static int Help()", StringComparison.Ordinal)..];
        help = help[..help.IndexOf("\"\"\");", StringComparison.Ordinal)];

        // `aiko serve` refuses --port as an unknown argument: the port is saved by the daemon (AIKO_PORT).
        Assert.DoesNotContain("serve [-d|--detached] [--port", help, StringComparison.Ordinal);

        // A user-scope connection is not tied to a project, so the project cannot be required there.
        Assert.Contains("agent install [--project <id>]", help, StringComparison.Ordinal);
        Assert.Contains("agent uninstall [--project <id>]", help, StringComparison.Ordinal);
    }

    internal static async Task<(int ExitCode, string Output)> RunCliAsync(
        string databasePath,
        string userHome,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(FindCli());
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Only this spec's data directory: an installed Aiko on the machine must not be touched or read.
        foreach (var name in startInfo.Environment.Keys.Where(key => key.StartsWith("AIKO_", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment["AIKO_DATABASE"] = databasePath;
        startInfo.Environment["AIKO_USER_HOME"] = userHome;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
        return (process.ExitCode, await output + await error);
    }

    /// <summary>The CLI built in the same configuration as this spec assembly.</summary>
    private static string FindCli()
    {
        var configuration = new DirectoryInfo(Path.GetDirectoryName(typeof(CliInitSpecs).Assembly.Location)!)
            .Parent!.Name;
        var cli = Path.Combine(FindRepositoryRoot(), "src", "Aiko.Cli", "bin", configuration, "net10.0", "aiko.dll");
        return File.Exists(cli) ? cli : throw new FileNotFoundException("Build Aiko.Cli first.", cli);
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(CliInitSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
