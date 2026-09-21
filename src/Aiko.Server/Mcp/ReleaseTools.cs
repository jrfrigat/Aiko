using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for the release history: reading it, and recording a release.
/// </summary>
/// <remarks>
/// Recording lives here and nowhere else. A release is recorded by the agent that conducted it, through the
/// procedure the project's scheme describes, so the tool is the one writer of the release document - and the
/// HTTP surface reads that document without offering to write it.
/// </remarks>
[McpServerToolType]
internal sealed class ReleaseTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IReleaseStore releases) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_releases", Title = "List Aiko releases")]
    [Description(
        "Lists the project's release history, newest first: each record carries its version tag, the scheme "
        + "it followed, when it was recorded and the cards that went into it. Read this before conducting a "
        + "release - the previous record is what says which cards have shipped since.")]
    public async Task<string> ListReleasesAsync(CancellationToken cancellationToken = default)
    {
        var document = await releases.ReadAsync(GetProjectId(), cancellationToken);
        return JsonSerializer.Serialize(document, ServerJsonContext.Default.ReleaseDocument);
    }

    [McpServerTool(Name = "aiko_record_release", Title = "Record an Aiko release")]
    [Description(
        "Records a release: the version tag, the scheme it followed, the cards that went into it and an "
        + "optional note. The list of cards is explicit - a card is in a release because it is named here, not "
        + "because of the state it was in that day. A version already recorded is refused rather than "
        + "overwritten: one version has one record, so \"which cards went into it\" has one answer.")]
    public async Task<string> RecordReleaseAsync(
        [Description("Version tag the release was published under, for example v0.1.3 or v0.1.3-pre.")]
        string version,
        [Description("Identifier of the release scheme the release followed, for example git-release.")]
        string schemeId,
        [Description("Cards that went into the release, by id. Pass an empty list when it carried none.")]
        string[]? cards = null,
        [Description("What to remember about this release, or null.")]
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var record = await releases.RecordAsync(
            GetProjectId(),
            new RecordReleaseRequest(version, schemeId, cards, notes),
            cancellationToken);
        return JsonSerializer.Serialize(record, ServerJsonContext.Default.ReleaseRecord);
    }
}
