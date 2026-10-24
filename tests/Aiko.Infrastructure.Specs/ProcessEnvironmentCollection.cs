using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The spec classes that change the environment of the test process - <c>PATH</c>, <c>AIKO_USER_HOME</c> -
/// and run apart from every other class.
/// </summary>
/// <remarks>
/// The environment belongs to the whole process, and xUnit runs the classes of one assembly side by side. While
/// one spec had <c>PATH</c> pointed at an empty temporary directory, another one's <c>Process.Start("dotnet")</c>
/// found no <c>dotnet</c>, and code reading <c>AIKO_USER_HOME</c> wrote into a home its owner was deleting -
/// failures that came and went with the timing and stopped a release. A class that sets an environment
/// variable, even one it puts back in <c>finally</c>, belongs to this collection.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Process environment";
}

/// <summary>Keeps the rule above from being forgotten by the next spec that sets a variable.</summary>
public sealed class ProcessEnvironmentCollectionSpecs
{
    private const string Setter = "Environment.SetEnvironmentVariable(";
    private const string Membership = "[Collection(ProcessEnvironmentCollection.Name)]";

    [Fact]
    public void Every_spec_file_that_changes_the_process_environment_runs_apart()
    {
        var tests = Path.Combine(CliInitSpecs.FindRepositoryRoot(), "tests");
        var offenders = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => File.ReadAllText(file).Contains(Setter, StringComparison.Ordinal))
            .Where(file => !File.ReadAllText(file).Contains(Membership, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(tests, file))
            .ToArray();

        // Another test assembly has no such collection, so a setter there is an offender too: its classes run
        // side by side as well.
        Assert.True(
            offenders.Length == 0,
            "These spec files set an environment variable without joining the process-environment " +
            $"collection: {string.Join(", ", offenders)}");
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
