using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// What <c>scripts/install.ps1</c> - the documented way to install and to update - may do to the directory it
/// is given. It is PowerShell run through <c>irm | iex</c>, so the rules are read from its text, the way the
/// release workflow is: a directory that is not Aiko's is never emptied, a running daemon is stopped before its
/// files are replaced, and a failed copy puts the previous installation back instead of leaving half of one.
/// </summary>
public sealed class InstallScriptSpecs
{
    [Fact]
    public void The_script_refuses_a_directory_that_holds_something_other_than_aiko()
    {
        var script = Script();

        Assert.Contains("function Assert-InstallDirIsAiko", script, StringComparison.Ordinal);
        Assert.Contains("holds no Aiko installation", script, StringComparison.Ordinal);
        // The check runs before the download: refusing costs nothing yet.
        Assert.True(
            script.IndexOf("Assert-InstallDirIsAiko $InstallDir", StringComparison.Ordinal) <
            script.IndexOf("Invoke-WebRequest", StringComparison.Ordinal),
            "the directory check should run before the download");
    }

    [Fact]
    public void The_script_stops_the_daemon_and_swaps_with_a_way_back()
    {
        var script = Script();

        // The old way emptied the directory entry by entry and stopped at the first locked file. What is left is
        // the rollback's clean-up of a failed copy, which must not stop half-way either.
        Assert.DoesNotContain(
            "Get-ChildItem -Path $InstallDir -Force | Remove-Item -Recurse -Force\n",
            script.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("Move-Item -Destination $previous", script, StringComparison.Ordinal);
        Assert.Contains("serve stop", script, StringComparison.Ordinal);
        Assert.Contains(".previous-", script, StringComparison.Ordinal);
        Assert.Contains("the previous installation was put back", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scripts", "install.ps1")]
    [InlineData("install.ps1")]
    public void The_functions_pass_the_two_analyzer_rules_the_ci_lint_gate_enforces(params string[] path)
    {
        // CI lints the installers with PSScriptAnalyzer at Warning severity, and nothing in dotnet test did, so
        // two warnings reached main unnoticed. These are the same two rules, read from the text, so they fail here
        // first: a noun is singular, and a function that changes state supports -WhatIf and -Confirm.
        var script = File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. path]));
        var offenders = new List<string>();
        foreach (Match function in FunctionHeader.Matches(script))
        {
            var verb = function.Groups["verb"].Value;
            var noun = function.Groups["noun"].Value;
            if (noun.EndsWith('s') && !SingularNounsEndingInS.Contains(noun))
            {
                offenders.Add($"{verb}-{noun}: plural noun");
            }

            if (StateChangingVerbs.Contains(verb) &&
                !Body(script, function.Index).Contains("SupportsShouldProcess", StringComparison.Ordinal))
            {
                offenders.Add($"{verb}-{noun}: changes state without SupportsShouldProcess");
            }
        }

        Assert.Empty(offenders);
    }

    private static readonly Regex FunctionHeader =
        new(@"^\s*function\s+(?<verb>[A-Za-z]+)-(?<noun>[A-Za-z]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The verbs PSUseShouldProcessForStateChangingFunctions treats as changing state.</summary>
    private static readonly HashSet<string> StateChangingVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "New", "Set", "Remove", "Start", "Stop", "Restart", "Reset", "Update" };

    /// <summary>Nouns that end in "s" without being plural; one goes here only when it truly is singular.</summary>
    private static readonly HashSet<string> SingularNounsEndingInS =
        new(StringComparer.OrdinalIgnoreCase) { "Status", "Alias" };

    /// <summary>The text of a function up to the next function header, where its CmdletBinding would be.</summary>
    private static string Body(string script, int start)
    {
        var next = FunctionHeader.Match(script, start + 1);
        return next.Success ? script[start..next.Index] : script[start..];
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "install.ps1"));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(InstallScriptSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
