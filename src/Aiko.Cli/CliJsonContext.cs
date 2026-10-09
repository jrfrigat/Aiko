using System.Text.Json.Serialization;
using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;

namespace Aiko.Cli;

/// <summary>
/// JSON the CLI has to build itself: a project's stored workflow definition, and the body the daemon's
/// workflow endpoint accepts.
/// </summary>
/// <remarks>
/// Settings are deliberately absent: those documents are read and written as the daemon's own JSON, so the
/// CLI keeps no second copy of their shape.
/// </remarks>
[JsonSerializable(typeof(WorkflowDefinition))]
[JsonSerializable(typeof(StageDefinition))]
[JsonSerializable(typeof(WorkflowUpdate))]
[JsonSerializable(typeof(TemplateFromProject))]
[JsonSerializable(typeof(TemplateExport))]
[JsonSerializable(typeof(TemplateImport))]
[JsonSerializable(typeof(TemplateApply))]
[JsonSerializable(typeof(AikoEvent))]
[JsonSerializable(typeof(IReadOnlyList<AikoEvent>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
internal sealed partial class CliJsonContext : JsonSerializerContext;

/// <summary>
/// The body of <c>PUT /api/v1/projects/{projectId}/workflows/{workflowId}</c>, mirrored field for field.
/// </summary>
/// <remarks>
/// The daemon's own request type is internal to it, so the field names are restated here. This is the one
/// place where the CLI repeats a daemon contract, and it is repeated on purpose: the alternative - guessing a
/// shape at the call site - is how a client comes to send a body the server quietly ignores half of. A rename
/// on either side shows up as the endpoint refusing the document.
/// </remarks>
/// <param name="Title">Display name of the workflow and of the card type it defines.</param>
/// <param name="Stages">The complete stage list, in the order it should be saved in.</param>
/// <param name="ExpectedRevision">Revision the caller read, for the optimistic check.</param>
/// <param name="Description">What the card type is for, or null for none.</param>
/// <param name="Icon">Card type icon id, or null for the default.</param>
/// <param name="Color">Card type colour id, or null for the default.</param>
/// <param name="BlendsWithParent">Whether a card of this type blends its score with its parents'.</param>
internal sealed record WorkflowUpdate(
    string Title,
    IReadOnlyList<StageDefinition> Stages,
    long ExpectedRevision,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    bool BlendsWithParent = false);

/// <summary>
/// The body of <c>POST /api/v1/templates/from-project</c>: a project's settings, pipelines, projections and
/// memory captured as a template, which is what "export a project's configuration" means here.
/// </summary>
/// <param name="ProjectId">Project to copy.</param>
/// <param name="TemplateId">Identifier for the new template.</param>
/// <param name="Name">Display name, or null for "&lt;project name&gt; template".</param>
internal sealed record TemplateFromProject(string ProjectId, string TemplateId, string? Name = null);

/// <summary>The body of <c>POST /api/v1/templates/{id}/export</c>: where to write the template's file.</summary>
/// <param name="Path">Absolute path of the file to write.</param>
internal sealed record TemplateExport(string Path);

/// <summary>
/// The body of <c>POST /api/v1/templates/import</c>: the file to read and, optionally, the id to store it
/// under - null keeps the id the file carries.
/// </summary>
/// <param name="Path">Absolute path of the file to read.</param>
/// <param name="TemplateId">Identifier to store it under, or null for the one the file carries.</param>
internal sealed record TemplateImport(string Path, string? TemplateId = null);

/// <summary>The body of <c>POST /api/v1/projects/{id}/apply-template</c>: the template to apply.</summary>
/// <param name="TemplateId">Template to apply.</param>
internal sealed record TemplateApply(string TemplateId);
