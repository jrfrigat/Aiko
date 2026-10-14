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
/// <c>Area</c> is a coarse group of the check - <c>data</c>, <c>token</c>, <c>endpoint</c>, <c>project</c>,
/// <c>agent-config</c> - and <c>Detail</c> is the path or value the finding is about.
/// </summary>
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
    /// Named here because more than one place reports these findings - <c>aiko doctor</c>, <c>aiko status</c>
    /// and the daemon screen of the UI - and a second spelling is how those lists come to disagree.
    /// </remarks>
    public const string AgentConfigArea = "agent-config";

    /// <summary>
    /// Area of the findings about the credential an agent's MCP entry authenticates with.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AgentConfigArea"/> on purpose: a configuration that is present and current but
    /// whose credential cannot be used is a different problem from one pointing at an old endpoint, and a
    /// count of drift by area would otherwise report a missing token as "written at an old address".
    /// </remarks>
    public const string AgentCredentialArea = "agent-credential";
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
/// endpoint, the registered projects and the agent configurations that point at this daemon. Deliberately
/// split from the repair - <c>aiko doctor</c> changes nothing and <c>aiko repair --fix</c> performs exactly
/// the actions this names - and the point of the agent check is that an MCP configuration records the
/// daemon's endpoint, so a moved port leaves those files pointing at nothing and nothing else would notice.
/// </summary>
public interface IWorkshopDiagnostics
{
    /// <summary>Inspects the installation, or only the one project when it is named.</summary>
    ValueTask<WorkshopDiagnostics> InspectAsync(string? projectId, CancellationToken cancellationToken);
}
