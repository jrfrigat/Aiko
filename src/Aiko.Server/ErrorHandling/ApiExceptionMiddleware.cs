using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Aiko.Application.Contracts;
using Aiko.Server.Contracts;

namespace Aiko.Server.ErrorHandling;

/// <summary>
/// Maps unhandled exceptions to stable HTTP responses in one place, so endpoints do not
/// repeat try/catch blocks. MCP routes are skipped: their tool errors are handled by the
/// MCP layer. The response is never rewritten after it has started (for example, an SSE
/// stream) - such requests are logged and aborted so the client can reconnect.
/// </summary>
internal static class ApiExceptionMiddleware
{
    public static IApplicationBuilder UseApiExceptionMapping(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/mcp"))
            {
                await next(context);
                return;
            }

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aiko.Server.ApiException");

            try
            {
                await next(context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (context.Response.HasStarted)
                {
                    logger.LogWarning(exception, "Exception after the response started; aborting the request.");
                    return;
                }

                var (statusCode, json) = MapException(exception);
                if (statusCode >= StatusCodes.Status500InternalServerError)
                {
                    logger.LogError(exception, "Unhandled server error.");
                }
                else
                {
                    logger.LogDebug(exception, "Mapped client error to status {Status}.", statusCode);
                }

                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(json, Encoding.UTF8);
            }
        });

    private static (int StatusCode, string Json) MapException(Exception exception)
    {
        if (exception is RevisionConflictException conflict)
        {
            return (
                StatusCodes.Status409Conflict,
                JsonSerializer.Serialize(
                    new RevisionConflictResponse(conflict.ExpectedRevision, conflict.ActualRevision),
                    ServerJsonContext.Default.RevisionConflictResponse));
        }

        if (exception is ArtifactConflictException artifactConflict)
        {
            return (
                StatusCodes.Status409Conflict,
                JsonSerializer.Serialize(
                    new ArtifactConflictResponse(artifactConflict.ExpectedVersion, artifactConflict.ActualVersion),
                    ServerJsonContext.Default.ArtifactConflictResponse));
        }

        if (exception is KeyNotFoundException notFound)
        {
            return (StatusCodes.Status404NotFound, Error(notFound.Message));
        }

        if (exception is ArgumentException or BadHttpRequestException or JsonException or IOException)
        {
            return (StatusCodes.Status400BadRequest, Error(exception.InnerException?.Message ?? exception.Message));
        }

        return (StatusCodes.Status500InternalServerError, Error("Internal server error."));
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new ErrorResponse(message), ServerJsonContext.Default.ErrorResponse);
}
