using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aiko.Server.Security;

/// <summary>
/// Enforces the local access token on the REST API and MCP endpoints. Static assets and the
/// health and pairing endpoints stay public. The token is accepted either through the
/// Authorization header or the HttpOnly session cookie issued by the pairing endpoint.
/// </summary>
internal static class AuthenticationMiddleware
{
    public static IApplicationBuilder UseAikoAuthentication(
        this IApplicationBuilder app,
        string accessToken,
        bool enabled)
    {
        if (!enabled)
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            if (IsPublic(context.Request.Path) || IsAuthenticated(context, accessToken))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Authentication required.");
        });
    }

    private static bool IsPublic(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/api/v1/auth/pair") ||
        (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/mcp"));

    private static bool IsAuthenticated(HttpContext context, string accessToken)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            TokenEquals(header["Bearer ".Length..].Trim(), accessToken))
        {
            return true;
        }

        return context.Request.Cookies.TryGetValue("aiko_session", out var cookie) &&
               TokenEquals(cookie, accessToken);
    }

    private static bool TokenEquals(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}