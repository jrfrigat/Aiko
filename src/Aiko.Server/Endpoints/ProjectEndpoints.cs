using Aiko.Application.Contracts;
using Aiko.Domain.Workflow;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Project endpoints: listing registered projects and initializing new ones.
/// </summary>
internal static class ProjectEndpoints
{
    /// <summary>
    /// Maps /api/v1/projects, /api/v1/projects/initialize and /api/v1/projects/{projectId}/reindex.
    /// </summary>
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects",
            async (IProjectCatalog catalog, CancellationToken cancellationToken) =>
                TypedResults.Ok(await catalog.ListAsync(cancellationToken)));
        app.MapPost(
            "/api/v1/projects/initialize",
            async Task<IResult> (
                InitializeProjectRequest request,
                IProjectInitializer initializer,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var project = await initializer.InitializeAsync(request, cancellationToken);
                    // The Location header carries the readable handle, which is what the UI links with.
                    return TypedResults.Created($"/api/v1/projects/{project.Handle}", project);
                }
                catch (InvalidOperationException exception)
                {
                    // A project id the user chose is already taken. The answer is a conflict naming the value,
                    // not a silent rename: the caller typed it and has to learn it was refused.
                    return Results.Conflict(new ErrorResponse(exception.Message));
                }
                catch (Exception exception) when (exception is DirectoryNotFoundException or ArgumentException)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
            });
        // The templates a project can be created from. Read-only: the only one that exists today is the
        // built-in default, and authoring templates is the post-MVP half of the feature.
        app.MapGet(
            "/api/v1/templates",
            async (IProjectTemplateStore templates, CancellationToken cancellationToken) =>
                TypedResults.Ok(await templates.ListAsync(cancellationToken)));
        // Copying is how a template is created: the base ships with Aiko and has no file, so an installation
        // that wants its own defaults copies it and edits the copy.
        app.MapPost(
            "/api/v1/templates",
            async (
                CreateTemplateRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var copy = await templates.CopyAsync(
                        request.SourceId,
                        request.TemplateId,
                        request.Name,
                        cancellationToken);
                    return Results.Created($"/api/v1/templates/{copy.Id}", copy);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound(new ErrorResponse(
                        $"No template '{request.SourceId}' to copy."));
                }
                catch (IOException exception)
                {
                    return Results.Conflict(new ErrorResponse(exception.Message));
                }
            });
        // One template in full: its settings and its pipelines. The defaults screen reads this, edits one
        // slice of it and writes that slice back, so the two writers never clobber each other.
        app.MapGet(
            "/api/v1/templates/{templateId}",
            async (
                string templateId,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
            });
        app.MapPut(
            "/api/v1/templates/{templateId}/settings",
            async (
                string templateId,
                AppSettings request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                // One version for the whole document: the settings are part of the template's content, and
                // projects record which version of it they were created from.
                await templates.WriteAsync(
                    template with { Settings = request, Version = template.Version + 1 },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });
        // What a template says about itself: separate from the settings write because it is a different
        // question - the settings are what a project runs with, this is what the template is.
        app.MapPut(
            "/api/v1/templates/{templateId}",
            async Task<IResult> (
                string templateId,
                UpdateTemplateRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                var name = request.Name is null ? template.Name : request.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Results.BadRequest(new ErrorResponse("A template name is required."));
                }

                var updated = template with
                {
                    Name = name,
                    Description = request.Description is null
                        ? template.Description
                        : request.Description.Trim(),
                    InitializationInstruction = request.InitializationInstruction is null
                        ? template.InitializationInstruction
                        : NormalizeInstruction(request.InitializationInstruction),
                    Version = template.Version + 1
                };
                await templates.WriteAsync(updated, cancellationToken);

                // Re-read rather than returning the object that was written: on a built-in template the write
                // creates a file, and the answer should say what the store now holds.
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });
        app.MapPut(
            "/api/v1/templates/{templateId}/workflows/{workflowId}",
            async (
                string templateId,
                string workflowId,
                UpdateWorkflowRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                if (WorkflowEndpoints.Validate(workflowId, request.Title, request.Stages) is { } invalid)
                {
                    return invalid;
                }

                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                var existing = template.Workflows.FirstOrDefault(workflow =>
                    StringComparer.Ordinal.Equals(workflow.Id, workflowId));
                if (existing is null)
                {
                    return Results.NotFound();
                }
                if (existing.Revision != request.ExpectedRevision)
                {
                    return Results.Conflict(new RevisionConflictResponse(
                        request.ExpectedRevision,
                        existing.Revision));
                }

                var updated = new WorkflowDefinition(
                    existing.Id,
                    request.Title.Trim(),
                    request.Stages.OrderBy(stage => stage.Order).ToArray(),
                    existing.Revision + 1,
                    string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    AppearanceCatalog.NormalizeIcon(request.Icon),
                    AppearanceCatalog.NormalizeColor(request.Color),
                    request.BlendsWithParent);
                // A template has no cards, so the "stage still contains cards" rule of the project endpoint
                // has nothing to check here: a template's pipeline is a starting point, not a live board.
                var workflows = template.Workflows
                    .Select(workflow => StringComparer.Ordinal.Equals(workflow.Id, workflowId) ? updated : workflow)
                    .ToArray();

                await templates.WriteAsync(
                    template with { Workflows = workflows, Version = template.Version + 1 },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });
        // A template's pipelines are card types too: a project created from this template starts with the
        // types its author defined here.
        app.MapPost(
            "/api/v1/templates/{templateId}/workflows",
            async (
                string templateId,
                CreateWorkflowRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                if (WorkflowEndpoints.Validate(request.Id, request.Title, request.Stages) is { } invalid)
                {
                    return invalid;
                }

                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                var id = request.Id.Trim().ToLowerInvariant();
                if (template.Workflows.Any(workflow => StringComparer.Ordinal.Equals(workflow.Id, id)))
                {
                    return Results.Conflict(new ErrorResponse($"Workflow '{id}' already exists."));
                }

                var created = new WorkflowDefinition(
                    id,
                    request.Title.Trim(),
                    request.Stages.OrderBy(stage => stage.Order).ToArray(),
                    1,
                    string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                    AppearanceCatalog.NormalizeIcon(request.Icon),
                    AppearanceCatalog.NormalizeColor(request.Color),
                    request.BlendsWithParent);

                await templates.WriteAsync(
                    template with
                    {
                        Workflows = [.. template.Workflows, created],
                        Version = template.Version + 1
                    },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });

        app.MapDelete(
            "/api/v1/templates/{templateId}/workflows/{workflowId}",
            async (
                string templateId,
                string workflowId,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                ProjectTemplate template;
                try
                {
                    template = await templates.ReadAsync(templateId, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }

                if (!template.Workflows.Any(workflow =>
                        StringComparer.Ordinal.Equals(workflow.Id, workflowId)))
                {
                    return Results.NotFound();
                }

                await templates.WriteAsync(
                    template with
                    {
                        Workflows = template.Workflows
                            .Where(workflow => !StringComparer.Ordinal.Equals(workflow.Id, workflowId))
                            .ToArray(),
                        Version = template.Version + 1
                    },
                    cancellationToken);
                return Results.Ok(await templates.ReadAsync(templateId, cancellationToken));
            });

        // Authoring a template: an installation captures a project it likes and starts the next ones from it.
        app.MapPost(
            "/api/v1/templates/from-project",
            async (
                CreateTemplateFromProjectRequest request,
                IProjectCatalog catalog,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                var project = await catalog.FindAsync(request.ProjectId, cancellationToken);
                if (project is null)
                {
                    return Results.NotFound();
                }

                try
                {
                    return Results.Ok(await templates.CreateFromProjectAsync(
                        project.RootPath,
                        request.TemplateId,
                        request.Name,
                        cancellationToken));
                }
                catch (IOException exception)
                {
                    return Results.Conflict(new ErrorResponse(exception.Message));
                }
            });
        // Import is how a starting point travels between installations: the file is the template.
        app.MapPost(
            "/api/v1/templates/import",
            async (
                ImportTemplateRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await templates.ImportAsync(
                        request.Path,
                        request.TemplateId,
                        cancellationToken));
                }
                catch (FileNotFoundException exception)
                {
                    return Results.NotFound(new ErrorResponse(exception.Message));
                }
                catch (IOException exception)
                {
                    return Results.Conflict(new ErrorResponse(exception.Message));
                }
                catch (InvalidDataException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
            });
        app.MapPost(
            "/api/v1/templates/{templateId}/export",
            async (
                string templateId,
                ExportTemplateRequest request,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await templates.ExportAsync(templateId, request.Path, cancellationToken);
                    return Results.Ok(new ErrorResponse(request.Path));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
            });
        // Deleting a template removes a file this installation owns. The shipped base has no file, so a
        // delete of it is a no-op that says so rather than a failure.
        app.MapDelete(
            "/api/v1/templates/{templateId}",
            async (
                string templateId,
                IProjectTemplateStore templates,
                CancellationToken cancellationToken) =>
                await templates.DeleteAsync(templateId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound());
        // Applying a template to a project that already exists: the one explicit exception to "a project is
        // autonomous after init", and it refuses rather than stranding a card.
        app.MapPost(
            "/api/v1/projects/{projectId}/apply-template",
            async (
                string projectId,
                ApplyTemplateRequest request,
                IProjectTemplateApplier applier,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await applier.ApplyAsync(projectId, request.TemplateId, cancellationToken));
                }
                catch (FileNotFoundException exception)
                {
                    return Results.NotFound(new ErrorResponse(exception.Message));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.BadRequest(new ErrorResponse(exception.Message));
                }
            });
        // The project's derived numbers: stage transitions per week and what the board is made of. Read
        // from SQLite, where the daemon records what it observed - never from the files it owns.
        app.MapGet(
            "/api/v1/projects/{projectId}/analytics",
            async (
                string projectId,
                int? weeks,
                IProjectAnalytics analytics,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await analytics.ReadAsync(projectId, weeks ?? 8, cancellationToken)));
        app.MapPost(
            "/api/v1/projects/{projectId}/reindex",
            async (
                string projectId,
                IProjectReindexer reindexer,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await reindexer.ReindexAsync(projectId, cancellationToken)));
        // Unregistering, not deleting: the registration and its SQLite projections go, the project's
        // files stay. A mistyped `aiko init` path has to be undoable, so this cannot be a destructive
        // operation by default.
        app.MapDelete(
            "/api/v1/projects/{projectId}",
            async Task<IResult> (
                string projectId,
                IProjectCatalog catalog,
                CancellationToken cancellationToken) =>
            {
                var removed = await catalog.RemoveAsync(projectId, cancellationToken);
                return removed ? TypedResults.NoContent() : TypedResults.NotFound();
            });
    }

    /// <summary>
    /// Trims an instruction and turns the empty string into null, so "no instruction" has one representation
    /// rather than two.
    /// </summary>
    private static string? NormalizeInstruction(string? instruction) =>
        string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim();
}
