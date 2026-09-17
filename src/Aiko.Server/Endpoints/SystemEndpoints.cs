using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aiko.Application.Contracts;
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
    /// How this daemon actually executes, as a caption for the cockpit's runtime tag.
    /// </summary>
    /// <remarks>
    /// The design's mock reads "NATIVE AOT", but the release workflow publishes the daemon with
    /// <c>-p:PublishAot=false</c> (the AOT toolchain would pull a C++ workload into the runner), so that
    /// tag would announce something the binary does not do. <see cref="RuntimeFeature.IsDynamicCodeSupported"/>
    /// is the honest answer: it is false only in a Native AOT build, so a source build reports JIT and an
    /// AOT publish would report itself without anyone having to remember to change it.
    /// </remarks>
    internal static string RuntimeCaption =>
        $"{RuntimeInformation.FrameworkDescription} · " +
        (RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT");

    /// <summary>
    /// Maps /health and /api/v1/system.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="baseUri">The loopback base URL the daemon listens on.</param>
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app, Uri baseUri)
    {
        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy")));
        app.MapGet(
            "/api/v1/system",
            async (IDaemonTelemetry telemetry, CancellationToken cancellationToken) => TypedResults.Ok(
                new SystemResponse(
                    "Aiko",
                    DaemonVersion,
                    RuntimeCaption,
                    Environment.ProcessId,
                    baseUri.ToString().TrimEnd('/'),
                    DateTimeOffset.UtcNow,
                    await telemetry.ReadAsync(cancellationToken))));
        // The same inspection `aiko doctor` prints, so the settings screen can report the installation's
        // own health without a terminal. Read-only by contract: IWorkshopDiagnostics changes nothing.
        app.MapGet(
            "/api/v1/system/diagnostics",
            async (IWorkshopDiagnostics diagnostics, CancellationToken cancellationToken) =>
                TypedResults.Ok(await diagnostics.InspectAsync(null, cancellationToken)));
    }
}
