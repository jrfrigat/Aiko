namespace Aiko.Installer;

/// <summary>
/// Turns the command line into <see cref="InstallerArguments"/>, or says what it could not understand.
/// </summary>
/// <remarks>
/// A refusal here is a usage error and nothing else: nothing has been read and nothing written, so the
/// program can say which argument it did not take and stop. That is why the parse is separate from the run
/// rather than folded into it - a mistyped flag must not look like a failed installation.
/// </remarks>
internal static class InstallerCommandLine
{
    /// <summary>What the program prints when it was asked for help, and after a usage error.</summary>
    public const string Usage = """
        aiko-installer - installs and updates Aiko.

        Usage: aiko-installer [install|update|check] [options]

        Verbs:
          install              Put the resolved release in place (the default).
          update               Bring an existing installation up to the release.
          check                Report what is installed and what is available, and change nothing.

        Options:
          --install-dir <path> Directory to install into; the installation's own bin by default.
          --tag <tag>          Release to install; the newest release by default.
          --no-path            Do not add the directory to the user PATH.
          --agents <id,id>     Connect these agents after the install.
          --no-agents          Connect no agent.
          --force              Allow replacing a newer installation with an older one.
          --yes                Accepted for the script that starts this; the program never asks.
          --version            Print the installed version and exit.
          -h, --help           Print this text.
        """;

    /// <summary>
    /// Reads the arguments, or reports the first one it cannot place.
    /// </summary>
    /// <param name="args">Arguments as the process received them.</param>
    /// <param name="request">The parsed request, when this returns true.</param>
    /// <param name="error">What was wrong, when this returns false.</param>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out InstallerArguments request,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verb = InstallationVerb.Install;
        var verbGiven = false;
        string? installDirectory = null;
        string? tag = null;
        var updatePath = true;
        IReadOnlyList<string>? agents = null;
        var noAgents = false;
        var force = false;
        var assumedYes = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "install" or "update" or "check":
                    // The verb may stand anywhere, but only one of them: a second word is a mistake worth
                    // naming rather than quietly taking as the verb.
                    if (verbGiven)
                    {
                        return Fail($"`{argument}` is a second verb; only one can be given.", out request, out error);
                    }

                    verbGiven = true;
                    verb = argument switch
                    {
                        "update" => InstallationVerb.Update,
                        "check" => InstallationVerb.Check,
                        _ => InstallationVerb.Install
                    };
                    break;

                case "--version":
                    verb = InstallationVerb.Version;
                    break;

                case "--install-dir":
                    if (!TryTakeValue(args, ref index, out installDirectory))
                    {
                        return Fail("`--install-dir` needs a path.", out request, out error);
                    }

                    break;

                case "--tag":
                    if (!TryTakeValue(args, ref index, out tag))
                    {
                        return Fail("`--tag` needs a release tag.", out request, out error);
                    }

                    break;

                case "--agents":
                    if (!TryTakeValue(args, ref index, out var list))
                    {
                        return Fail(
                            "`--agents` needs one or more adapter identifiers.",
                            out request,
                            out error);
                    }

                    agents = list.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;

                case "--no-agents":
                    noAgents = true;
                    break;

                case "--no-path":
                    updatePath = false;
                    break;

                case "--force":
                    force = true;
                    break;

                case "--yes" or "-y":
                    assumedYes = true;
                    break;

                default:
                    return Fail($"Unknown argument: `{argument}`.", out request, out error);
            }
        }

        request = new InstallerArguments(
            verb,
            installDirectory,
            tag,
            updatePath,
            agents,
            noAgents,
            force,
            assumedYes);
        error = null;
        return true;
    }

    /// <summary>
    /// Takes the value that follows a flag. A value that looks like another flag is refused rather than
    /// eaten: `--tag --force` is a missing tag, not a release called "--force".
    /// </summary>
    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string value)
    {
        if (index + 1 < args.Count && !args[index + 1].StartsWith('-'))
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool Fail(string message, out InstallerArguments request, out string? error)
    {
        request = new InstallerArguments(InstallationVerb.Install, null, null, true, null, false, false, false);
        error = message;
        return false;
    }
}
