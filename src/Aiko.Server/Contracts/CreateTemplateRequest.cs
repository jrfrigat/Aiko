namespace Aiko.Server.Contracts;

/// <summary>
/// Request to copy a project template, which is how a shipped base template gets its own editable copy.
/// </summary>
/// <param name="SourceId">Template to copy.</param>
/// <param name="TemplateId">Identifier for the copy, file-safe and not in use yet.</param>
/// <param name="Name">Display name for the copy, or null for "&lt;source name&gt; copy".</param>
public sealed record CreateTemplateRequest(string SourceId, string TemplateId, string? Name = null);
