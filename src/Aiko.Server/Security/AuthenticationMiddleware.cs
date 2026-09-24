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
/// <remarks>
/// The cookie is the browser's, and the browser sends it to every port of localhost - so it opens only what
/// the page itself uses. MCP is for agents, which carry the token, and never takes the cookie; and a write the
/// cookie authenticates must carry <c>X-Aiko-Request</c>, a header a form or a simple request from another
/// page cannot add without the preflight the origin check then refuses.
/// </remarks>
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
            if (IsPublic(context.Request.Path) || HasBearer(context, accessToken))
            {
                await next(context);
                return;
            }

            if (!context.Request.Path.StartsWithSegments("/mcp") && HasSessionCookie(context, accessToken))
            {
                if (!HttpMethods.IsGet(context.Request.Method) &&
                    !HttpMethods.IsHead(context.Request.Method) &&
                    !HttpMethods.IsOptions(context.Request.Method) &&
                    !context.Request.Headers.ContainsKey("X-Aiko-Request"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("A write from the browser must carry X-Aiko-Request.");
                    return;
                }

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

    private static bool HasBearer(HttpContext context, string accessToken)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
               TokenEquals(header["Bearer ".Length..].Trim(), accessToken);
    }

    private static bool HasSessionCookie(HttpContext context, string accessToken) =>
        context.Request.Cookies.TryGetValue("aiko_session", out var cookie) &&
        TokenEquals(cookie, accessToken);

    private static bool TokenEquals(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}