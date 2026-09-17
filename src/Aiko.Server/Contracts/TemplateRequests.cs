namespace Aiko.Server.Contracts;

/// <summary>
/// Creates a template out of a project: the project's settings, pipelines, projections and memory become the
/// starting point of the next projects.
/// </summary>
/// <param name="ProjectId">Project to copy.</param>
/// <param name="TemplateId">Identifier for the new template.</param>
/// <param name="Name">Display name, or null for "&lt;project name&gt; template".</param>
public sealed record CreateTemplateFromProjectRequest(string ProjectId, string TemplateId, string? Name = null);

/// <summary>
/// Writes a template's document to a file, so it can be handed to another installation.
/// </summary>
/// <param name="Path">Absolute path of the file to write.</param>
public sealed record ExportTemplateRequest(string Path);

/// <summary>
/// Reads a template document from a file.
/// </summary>
/// <param name="Path">Absolute path of the file to read.</param>
/// <param name="TemplateId">
/// Identifier to store it under, or null to keep the one the file carries - which is how the same file is
/// imported twice without overwriting the first import.
/// </param>
public sealed record ImportTemplateRequest(string Path, string? TemplateId = null);

/// <summary>
/// Applies a template to a project that already exists.
/// </summary>
/// <param name="TemplateId">Template to apply.</param>
public sealed record ApplyTemplateRequest(string TemplateId);
