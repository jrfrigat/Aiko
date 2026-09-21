using System.Diagnostics;
using System.Text;

namespace AikoInstaller;

/// <summary>
/// The exe face of the release installer: it carries <c>scripts/install.ps1</c> inside itself and runs it
/// with the Windows PowerShell host that is already on the machine.
/// </summary>
/// <remarks>
/// There is no installation logic here on purpose. Downloading the release, unpacking it, the PATH entry,
/// the questions and the CLI calls all belong to the script, which is embedded at build time - so the exe
/// and the engine it runs are the same commit and cannot drift away from each other.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The logical name the engine is embedded under. It is fixed by the project file rather than derived
    /// from the file's path, so moving the project cannot quietly break the lookup.
    /// </summary>
    private const string EngineResourceName = "install.ps1";

    private static int Main(string[] args)
    {
        if (!File.Exists(PowerShellPath))
        {
            Console.Error.WriteLine(
                $"Windows PowerShell was not found at {PowerShellPath}; install from the release ZIP instead.");
            return 1;
        }

        var engine = ReadEngine();
        if (engine is null)
        {
            return 1;
        }

        // A file rather than the engine's text on the host's stdin: the engine asks questions with
        // Read-Host, and stdin is exactly what carrying the text there would occupy.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"aiko-install-{Guid.NewGuid():N}.ps1");
        try
        {
            // With a BOM on purpose: Windows PowerShell reads a BOM-less file as ANSI, which would corrupt
            // the engine the day a non-ASCII line is written into it.
            File.WriteAllText(scriptPath, engine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return RunEngine(scriptPath, args);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    /// <summary>
    /// Reads the embedded engine, or reports why it could not and returns null.
    /// </summary>
    private static string? ReadEngine()
    {
        using var resource = typeof(Program).Assembly.GetManifestResourceStream(EngineResourceName);
        if (resource is null)
        {
            // Reachable only if the resource name and the project file disagree. Said in words rather than
            // thrown: the person running this has nothing to act on in a stack trace.
            Console.Error.WriteLine(
                "This build carries no installer script inside it; install from the release ZIP instead.");
            return null;
        }

        using var reader = new StreamReader(resource, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Runs the embedded engine, handing it this exe's own arguments and returning its exit code.
    /// </summary>
    /// <remarks>
    /// The arguments are passed through verbatim rather than re-declared here: a second list of the engine's
    /// options would drift from the first the moment the engine gains one, and a wrapper that knows half the
    /// options is worse than one that claims none. The price is that a typo reaches the engine and is
    /// reported in its words - which is the bargain the documented `irm ... | iex` path already makes.
    /// </remarks>
    private static int RunEngine(string scriptPath, string[] args)
    {
        var startInfo = new ProcessStartInfo(PowerShellPath)
        {
            // The engine's questions need a console, so nothing is redirected and the child inherits this
            // process's handles.
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        // The script is a temporary file and therefore unsigned, so the default RemoteSigned policy would
        // refuse it on a machine nobody has reconfigured.
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("Could not start Windows PowerShell to run the installer.");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Removes the extracted engine on the way out, best effort: a leftover temp file is untidy, while
    /// failing to remove it must not replace the installer's own result with a different one.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not remove {path}; it can be deleted by hand.");
        }
    }

    /// <summary>
    /// The host, named by full path: whatever happens to be called <c>powershell.exe</c> earlier on PATH is
    /// not something an installer should hand a script to.
    /// </summary>
    private static string PowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");
}
