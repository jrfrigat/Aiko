using System.ComponentModel;
using System.Text.Json;
using Aiko.Application.Contracts;
using Aiko.Application.Releases;
using Aiko.Server.Contracts;
using ModelContextProtocol.Server;

namespace Aiko.Server.Mcp;

/// <summary>
/// MCP tools for the release plans: reading them with their readiness, changing the composition of a version,
/// and closing the plan of a version that has been released.
/// </summary>
/// <remarks>
/// Writing lives here and nowhere else, exactly as it does for the release history: a plan is moved by the
/// agent that is planning, and the HTTP surface reads it without offering to write it. Unlike the history, a
/// plan is moved while the work is still ahead, so the tools are the only way the person's plan changes - there
/// is no second writer to keep in step.
/// </remarks>
[McpServerToolType]
internal sealed class ReleasePlanTools(
    IHttpContextAccessor httpContextAccessor,
    IProjectCatalog projects,
    IReleasePlanStore plans,
    ReleasePlanReadinessProjector readiness) : ProjectToolBase(httpContextAccessor, projects)
{
    [McpServerTool(Name = "aiko_list_release_plans", Title = "List Aiko release plans")]
    [Description(
        "Lists the project's release plans, most recently created first, each with how far it is from being "
        + "releasable: every planned card is reported as finished, blocked, in work or missing, with the cards "
        + "that hold a blocked one. Read this before conducting a release - the plan of the version is where "
        + "the composition comes from - and before planning work, because it says which version new cards "
        + "belong to. Nothing here is stored: it is read from the cards as they stand.")]
    public async Task<string> ListReleasePlansAsync(CancellationToken cancellationToken = default)
    {
        var report = await readiness.ReadAsync(GetProjectId(), cancellationToken);
        return JsonSerializer.Serialize(report, ServerJsonContext.Default.ReleasePlanDocumentReport);
    }

    [McpServerTool(Name = "aiko_update_release_plan", Title = "Change an Aiko release plan")]
    [Description(
        "Changes what a version is waiting for: creates its plan when it does not exist, adds and removes "
        + "cards, sets or clears the mark that makes it the plan new cards flow into, and stores a note. "
        + "Only one plan is current at a time - marking this one current takes the mark off every other "
        + "unreleased plan. A plan whose version has already been recorded is closed and refuses the change: "
        + "plan that work in another version instead. The plan is created on first use, so a version nobody "
        + "has planned yet is not an error.")]
    public async Task<string> UpdateReleasePlanAsync(
        [Description("Version whose plan is being changed, for example v0.2.0. It is a git tag, so it starts with 'v'.")]
        string version,
        [Description(
            "Identifier of the release scheme the version will follow, for example git-release. Required only "
            + "when the plan is being created; a plan that exists keeps the scheme it has.")]
        string? schemeId = null,
        [Description("Cards to plan for this version, by id. A card already planned is left where it is.")]
        string[]? addCards = null,
        [Description("Cards to take out of this version's plan, by id.")]
        string[]? removeCards = null,
        [Description(
            "True to make this the plan new cards flow into, false to stop it being one, null to leave the "
            + "mark as it is.")]
        bool? isCurrent = null,
        [Description(
            "What to remember about this version, or null to leave the stored note alone. An empty string "
            + "clears it.")]
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await plans.UpdateAsync(
            GetProjectId(),
            new ReleasePlanUpdate(version, schemeId, addCards, removeCards, isCurrent, notes),
            cancellationToken);
        return JsonSerializer.Serialize(plan, ServerJsonContext.Default.ReleasePlan);
    }

    [McpServerTool(Name = "aiko_close_release_plan", Title = "Close an Aiko release plan")]
    [Description(
        "Closes the plan of a version that has been recorded, and carries the work that outlived it forward: "
        + "the cards named here move into the plan of the version the caller names, or into the next "
        + "unreleased plan, each marked with the version it came from. When there is nowhere to carry them "
        + "they stay on the closed plan, still marked, rather than dropping out of the project's plans. Call "
        + "this right after recording a release: a closed plan is the answer to what that version was waiting "
        + "for, and it no longer changes.")]
    public async Task<string> CloseReleasePlanAsync(
        [Description("Version whose plan is being closed, for example v0.2.0.")]
        string version,
        [Description(
            "The plan's cards that did not finish, by id. An empty list says the version shipped everything it "
            + "planned.")]
        string[]? cardsToCarry = null,
        [Description(
            "Version that takes the unfinished cards, or null to let Aiko choose the next unreleased plan. A "
            + "version that is not planned yet is planned by this call.")]
        string? carryIntoVersion = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> carry = cardsToCarry ?? [];
        var plan = await plans.CloseAsync(
            GetProjectId(),
            version,
            carry,
            carryIntoVersion,
            cancellationToken);
        return JsonSerializer.Serialize(plan, ServerJsonContext.Default.ReleasePlan);
    }
}
