namespace Aiko.Application.Contracts;

/// <summary>
/// How much attention a diagnostic finding needs.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Checked and healthy.</summary>
    Ok,

    /// <summary>Something is off but Aiko still works; a repair can fix it.</summary>
    Warning,

    /// <summary>Aiko cannot work in this state until it is repaired.</summary>
    Error
}

/// <summary>
/// One line of the workshop report: what was checked, how it came out, and the path it is about.
/// </summary>
/// <param name="Area">Coarse group of the check: <c>data</c>, <c>token</c>, <c>endpoint</c>, <c>project</c>, <c>agent-config</c>.</param>
/// <param name="Severity">How much attention it needs.</param>
/// <param name="Summary">Human-readable line, including the fix when there is one.</param>
/// <param name="Detail">The path or value the finding is about, when there is one.</param>
public sealed record DiagnosticFinding(
    string Area,
    DiagnosticSeverity Severity,
    string Summary,
    string? Detail = null)
{
    /// <summary>
    /// Area of the findings about an agent's MCP configuration: the files that record this daemon's
    /// endpoint and go stale the moment its port moves.
    /// </summary>
    /// <remarks>
    /// Named here because more than one place reports these findings - <c>aiko doctor</c> prints them,
    /// <c>aiko status</c> and the daemon screen of the UI surface the same ones - and a second spelling of
    /// the value is exactly how those lists come to disagree.
    /// </remarks>
    public const string AgentConfigArea = "agent-config";
}

/// <summary>
/// Result of a workshop inspection.
/// </summary>
public sealed record WorkshopDiagnostics(IReadOnlyList<DiagnosticFinding> Findings)
{
    /// <summary>Whether anything needs attention.</summary>
    public bool HasProblems =>
        Findings.Any(finding => finding.Severity != DiagnosticSeverity.Ok);
}

/// <summary>
/// Read-only inspection of a local Aiko installation: the data directory, the access token, the daemon
/// endpoint, the registered projects and the agent configurations that point at this daemon.
/// </summary>
/// <remarks>
/// Deliberately split from the repair: <c>aiko doctor</c> changes nothing, and <c>aiko repair --fix</c>
/// performs exactly the actions the report names. The interesting check is the last one - an agent's MCP
/// configuration records the daemon's endpoint, so when the port moves those files silently point at
/// nothing, and nothing else in the product would notice.
/// </remarks>
public interface IWorkshopDiagnostics
{
    /// <summary>
    /// Inspects the installation, optionally narrowed to one project.
    /// </summary>
    /// <param name="projectId">A registered project to inspect, or null for all of them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<WorkshopDiagnostics> InspectAsync(string? projectId, CancellationToken cancellationToken);
}
