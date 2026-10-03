using System.Diagnostics;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;

namespace Aiko.Infrastructure.Cards;

/// <summary>
/// Opens a card artifact with the machine's own programs: the file manager at the file's folder, or the
/// code editor with the file. The path is resolved exactly as the artifact store resolves it, so an open
/// never reaches outside the card's directory.
/// </summary>
public sealed class ProcessArtifactOpener(
    IProjectCatalog projects,
    ICardStore cards) : IArtifactOpener
{
    /// <inheritdoc />
    public async ValueTask OpenAsync(
        CardReference card,
        string path,
        ArtifactOpenTarget target,
        CancellationToken cancellationToken)
    {
        var cardDirectory = await CardArtifactPaths.FindCardDirectoryAsync(projects, cards, card, cancellationToken);
        var artifactPath = CardArtifactPaths.ResolveArtifactPath(cardDirectory, path);
        CardArtifactPaths.EnsureNoReparsePoints(cardDirectory, artifactPath);
        Process.Start(CreateStartInfo(target, artifactPath));
    }

    /// <summary>The program to run for each target, with the one path the call is allowed to name.</summary>
    private static ProcessStartInfo CreateStartInfo(ArtifactOpenTarget target, string artifactPath) =>
        target switch
        {
            // The folder the file lies in: the file manager opens there whether the file exists yet or not.
            ArtifactOpenTarget.Explorer => ProgramStartInfo(FileManager, Path.GetDirectoryName(artifactPath)!),
            ArtifactOpenTarget.VisualStudioCode => ProgramStartInfo("code", artifactPath),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    private static string FileManager =>
        OperatingSystem.IsWindows() ? "explorer.exe"
        : OperatingSystem.IsMacOS() ? "open"
        : "xdg-open";

    private static ProcessStartInfo ProgramStartInfo(string program, string argument)
    {
        // A shell starts the program the way the user's own shell would, which is how "code" resolves to its
        // launcher without Aiko deciding where it is installed. The path travels as its own argument.
        var startInfo = new ProcessStartInfo { FileName = program, UseShellExecute = true };
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
