using System.Reflection;
using Aiko.Server.Contracts;

namespace Aiko.Server.Endpoints;

/// <summary>
/// System endpoints: the health check and daemon information.
/// </summary>
internal static class SystemEndpoints
{
    /// <summary>
    /// The version the daemon announces: the running assembly's informational version, which the release
    /// workflow sets from the tag (`-p:Version=0.3.0`). It used to be a literal, so a released binary
    /// reported a version it was not - in <c>/api/v1/system</c>, in the UI's version tag and in
    /// <c>aiko status</c>.
    /// </summary>
    internal static string DaemonVersion => Format(
        typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SystemEndpoints).Assembly.GetName().Version?.ToString()
        ?? "0.0.0");

    // The SDK appends the source revision to the informational version ("0.3.0+abc1234"). That suffix
    // belongs to build metadata, not to a version a person reads in the top bar.
    private static string Format(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>
    /// Maps /health and /api/v1/system.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="baseUri">The loopback base URL the daemon listens on.</param>
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app, Uri baseUri)
    {
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy")));
        app.MapGet("/api/v1/system", () => TypedResults.Ok(new SystemResponse(
            "Aiko",
            DaemonVersion,
            Environment.ProcessId,
            baseUri.ToString().TrimEnd('/'),
            DateTimeOffset.UtcNow)));
    }
}
