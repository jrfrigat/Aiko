using System.Xml.Linq;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// Guards the installer launcher, which is the one piece of the release that no compiler and no unit test
/// would otherwise notice: the exe runs a PowerShell script on purpose, so the interesting half of it is a
/// resource inside the binary rather than code a spec can call.
/// </summary>
/// <remarks>
/// The launcher's behaviour is verified by running it, not here - it holds no logic worth testing by
/// design. What these facts protect are the two links that fail <em>silently</em>: an engine that stopped
/// being embedded is discovered only when someone runs the downloaded exe, and a release workflow that
/// forgot to publish the launcher is discovered only at tag time, in a job nobody runs locally.
/// </remarks>
public sealed class InstallerLauncherSpecs
{
    [Fact]
    public void The_launcher_embeds_the_installer_script_and_reads_it_from_its_own_binary()
    {
        var project = Path.Combine(RepoRoot, "scripts", "AikoInstaller", "AikoInstaller.csproj");
        var embedded = XDocument.Load(project)
            .Descendants()
            .Where(element => element.Name.LocalName is "EmbeddedResource")
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Include")
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains(embedded, include => include.EndsWith("install.ps1", StringComparison.Ordinal));

        // And the other half of the same claim: the exe takes the engine from its own manifest rather than
        // fetching it. A launcher that downloaded the script would run whatever `main` holds that day, which
        // is the drift the embedded copy exists to prevent.
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "scripts", "AikoInstaller", "Program.cs"));
        Assert.Contains("GetManifestResourceStream", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_workflow_publishes_the_launcher_and_refuses_a_release_without_it()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

        // Published at all.
        Assert.Contains("dotnet publish scripts/AikoInstaller", workflow, StringComparison.Ordinal);
        // Checked where the release is checked, so a publish that produced nothing fails there rather than at
        // the upload.
        Assert.Contains("'publish/installer/aiko-installer.exe'", workflow, StringComparison.Ordinal);
        // Handed to `gh release create` / `gh release upload`.
        Assert.Contains(
            "INSTALLER: publish/installer/aiko-installer.exe",
            workflow,
            StringComparison.Ordinal);
        // And required before the draft is made visible.
        Assert.Contains("*\"aiko-installer.exe\"*)", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_solution_builds_the_launcher_so_ci_compiles_it()
    {
        // Without this a broken launcher surfaces only in the release job at tag time; both CI jobs build
        // the solution, and neither would see a project that is not in it.
        var solution = File.ReadAllText(Path.Combine(RepoRoot, "Aiko.slnx"));
        Assert.Contains(
            "scripts/AikoInstaller/AikoInstaller.csproj",
            solution,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The repository root, found by walking up to the solution file. The specs run from the build output
    /// directory, so nothing under the repository can be addressed by a relative path.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("No Aiko.slnx above the spec's output directory.");
        }
    }
}
