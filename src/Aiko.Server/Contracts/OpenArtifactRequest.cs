using Aiko.Application.Contracts;

namespace Aiko.Server.Contracts;

/// <summary>
/// Opens a card artifact in a program on the daemon's machine.
/// </summary>
internal sealed record OpenArtifactRequest(
    string Path,
    ArtifactOpenTarget Target);
