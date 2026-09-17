using System.Text.RegularExpressions;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards the Razor markup itself. Razor treats text shaped like an email address as plain text, so an
/// expression written straight after a word renders literally: `v@State.System.Version` shipped in
/// v0.3.0 as the version tag in the top bar, and nothing else in the build notices - the page compiles,
/// it just displays source code to the user.
/// </summary>
public sealed class RazorMarkupSpecs
{
    // A word character immediately followed by @ and an identifier. Razor's email heuristic wins in that
    // shape, so the expression is never evaluated.
    private static readonly Regex EmailShapedExpression =
        new(@"[A-Za-z0-9_]@[A-Za-z_]", RegexOptions.Compiled);

    // Razor comments never render, and they are exactly where this pattern gets explained.
    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void No_razor_expression_is_written_where_razor_reads_an_email_address()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(FindRepositoryRoot(), "src", "Aiko.Pwa"),
                     "*.razor",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // Comments are blanked rather than removed so the reported line numbers stay right.
            var text = RazorComment.Replace(
                File.ReadAllText(file),
                match => new string('\n', match.Value.Count(character => character == '\n')));
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (EmailShapedExpression.IsMatch(lines[index]))
                {
                    offenders.Add($"{file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Razor reads these as email-looking text and renders them literally; write the expression as " +
            "@($\"...{value}\") instead:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Walks up from this assembly to the solution file, the same way the daemon fixture does.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(RazorMarkupSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
