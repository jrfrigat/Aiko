using System.Reflection;
using Aiko.Application.Installation;
using Aiko.Installer;
using Aiko.Infrastructure.Installation;
using Aiko.Infrastructure.Storage;

// What the bootstrap script and a person type. Parsed before anything is read or written, so a mistyped
// flag costs a message and nothing else.
if (args.Any(argument => argument is "-h" or "--help"))
{
    Console.WriteLine(InstallerCommandLine.Usage);
    return 0;
}

if (!InstallerCommandLine.TryParse(args, out var request, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(InstallerCommandLine.Usage);
    return 2;
}

var dataPaths = AikoDataPaths.FromEnvironment();
var installDirectory = request.InstallDirectory ?? new AikoInstallation(dataPaths).BinDirectory;

if (request.Verb is InstallationVerb.Version)
{
    // What is installed, and this build's own version when nothing is: an installer asked for a version
    // before it installed anything owes an answer, not an error.
    Console.WriteLine(InstalledVersionFile.Read(installDirectory)?.Tag ?? BuildVersion());
    return 0;
}

try
{
    var engine = await InstallationHost.CreateEngineAsync(CancellationToken.None);
    var installationRequest = request.ToInstallationRequest(installDirectory);
    var report = request.Verb is InstallationVerb.Update
        ? await engine.UpdateAsync(installationRequest, CancellationToken.None)
        : await engine.InstallAsync(installationRequest, CancellationToken.None);

    PrintReport(report);
    return report.Outcome is InstallationOutcome.Refused ? 1 : 0;
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    // A fault is not a decision. The engine reports what it decided and lets what broke escape, so the two
    // have to look different to whatever started this - a script that treats a crash as a refusal would
    // report a decision nobody made.
    Console.Error.WriteLine($"The run did not finish: {exception.Message}");
    return 3;
}

static string BuildVersion() =>
    typeof(InstallerArguments).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion
    ?? typeof(InstallerArguments).Assembly.GetName().Version?.ToString()
    ?? "unknown";

static void PrintReport(InstallationReport report)
{
    foreach (var step in report.Steps)
    {
        Console.WriteLine($"{(step.Succeeded ? "ok  " : "stop")} {step.Name}: {step.Detail}");
    }

    Console.WriteLine();
    Console.WriteLine(report.Summary);

    if (report.Installed is { } installed)
    {
        Console.WriteLine($"installed: {installed.Tag} in {installed.InstallDirectory}");
    }

    if (report.Available is { } available)
    {
        Console.WriteLine($"available: {available.Tag}");
    }
}
