using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// Settings endpoints: reading and saving project-local settings and the resolved effective view with the
/// source of each value. There is no installation-level document: the defaults a project starts from are
/// the template's, and they are copied into the project at init.
/// </summary>
/// <remarks>
/// Two documents live here and they are not interchangeable: the <em>view</em> (what a screen reads - the
/// effective values and the source of each) and the <em>document</em> (what a client writes). They used to
/// share one route, so a client that read the view and sent the same body back wrote an empty document -
/// every section was dropped without a word and the project lost its execution and priority settings. The
/// document now has its own symmetric route, and a body carrying a field the schema does not know is
/// refused rather than partly understood.
/// </remarks>
internal static class SettingsEndpoints
{
    /// <summary>
    /// The fields a settings document carries. Any other name means the caller sent something else - the
    /// view, or a misspelling - and saying so is the only safe answer.
    /// </summary>
    private static readonly HashSet<string> DocumentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "schemaVersion",
        "execution",
        "priority",
        "crossProject",
        "release"
    };

    /// <summary>
    /// Reads request bodies the way the JSON endpoints write them - the source-generated context this host
    /// serves every other body with - and refuses an unmapped member at any depth, so a field misspelled
    /// inside a section is a refusal too and not a value silently dropped.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadOptions = new(ServerJsonContext.Default.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>The document's type info, so reading a body needs no reflection and survives trimming.</summary>
    private static readonly JsonTypeInfo<AppSettings> DocumentTypeInfo =
        PayloadOptions.GetTypeInfo(typeof(AppSettings)) as JsonTypeInfo<AppSettings>
        ?? throw new InvalidOperationException("The settings document type is missing from the JSON context.");

    /// <summary>
    /// Maps /api/v1/projects/{projectId}/settings and its document route.
    /// </summary>
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // The view: what the project resolves to, and where each value came from. A screen reads this.
        app.MapGet(
            "/api/v1/projects/{projectId}/settings",
            async (
                string projectId,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await settings.LoadAsync(projectId, cancellationToken)));

        // The document, on a route of its own: what GET hands out is exactly what PUT accepts, so a client
        // can read it, change one value and send it back. A project that states nothing answers with an
        // empty document, which writes back as the same absence rather than as a wipe.
        app.MapGet(
            "/api/v1/projects/{projectId}/settings/document",
            async (
                string projectId,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
            {
                var view = await settings.LoadAsync(projectId, cancellationToken);
                return TypedResults.Ok(view.Snapshot ?? EmptyDocument());
            });

        app.MapPut(
            "/api/v1/projects/{projectId}/settings",
            async (
                string projectId,
                HttpRequest request,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
            {
                var (document, refusal) = await ReadDocumentAsync(request, cancellationToken);
                if (document is null)
                {
                    return Results.BadRequest(new ErrorResponse(refusal!));
                }

                await settings.SaveProjectAsync(projectId, document, cancellationToken);
                return TypedResults.Ok(await settings.LoadAsync(projectId, cancellationToken));
            });

        app.MapPut(
            "/api/v1/projects/{projectId}/settings/document",
            async (
                string projectId,
                HttpRequest request,
                IAppSettingsService settings,
                CancellationToken cancellationToken) =>
            {
                var (document, refusal) = await ReadDocumentAsync(request, cancellationToken);
                if (document is null)
                {
                    return Results.BadRequest(new ErrorResponse(refusal!));
                }

                await settings.SaveProjectAsync(projectId, document, cancellationToken);
                var view = await settings.LoadAsync(projectId, cancellationToken);
                return TypedResults.Ok(view.Snapshot ?? document);
            });
    }

    /// <summary>The document a project that states nothing answers with.</summary>
    private static AppSettings EmptyDocument() => new(AppSettings.CurrentSchemaVersion);

    /// <summary>
    /// Reads a settings document out of a request body, refusing anything that is not one.
    /// </summary>
    /// <remarks>
    /// The body is read here rather than bound to a parameter: the field names have to be seen before the
    /// binder can drop them, and a raw <c>JsonElement</c> parameter would ask this host's trimmed JSON
    /// context for metadata it does not carry. The check is the point - the binder drops properties the type
    /// does not declare, so a body of the wrong shape used to bind to an all-null document and overwrite the
    /// project's settings with it, which is how a read-then-write round trip emptied a project.
    /// </remarks>
    private static async ValueTask<(AppSettings? Document, string? Refusal)> ReadDocumentAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        JsonDocument parsed;
        try
        {
            parsed = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            return (null, $"The settings document could not be read: {exception.Message}");
        }

        using (parsed)
        {
            var body = parsed.RootElement;
            if (body.ValueKind is not JsonValueKind.Object)
            {
                return (null, "A settings document must be a JSON object.");
            }

            foreach (var property in body.EnumerateObject())
            {
                if (!DocumentFields.Contains(property.Name))
                {
                    return (null,
                        $"Unknown settings field '{property.Name}'. A document states schemaVersion, "
                        + "execution, priority, crossProject and release; the resolved view is read from "
                        + "GET /settings and is not a document.");
                }
            }

            try
            {
                var document = body.Deserialize(DocumentTypeInfo);
                return document is null
                    ? (null, "A settings document must be a JSON object.")
                    : (document, null);
            }
            catch (JsonException exception)
            {
                return (null, $"The settings document could not be read: {exception.Message}");
            }
        }
    }
}
