using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Agents;
using Aiko.Infrastructure.Cards;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Discussion;
using Aiko.Infrastructure.Execution;
using Aiko.Infrastructure.Events;
using Aiko.Infrastructure.Git;
using Aiko.Infrastructure.Memory;
using Aiko.Infrastructure.Projects;
using Aiko.Infrastructure.Relations;
using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Aiko.Server.Contracts;
using Aiko.Server.Diagnostics;
using Aiko.Server.Endpoints;
using Aiko.Server.ErrorHandling;
using Aiko.Server.Mcp;
using Aiko.Server.Security;

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
#if DEBUG
builder.WebHost.UseStaticWebAssets();
#endif

// A daemon started in the background has no console: AIKO_LOG_FILE gives it a file instead, so the
// question "why did it stop?" has an answer in the daemon's own words - including the host's own
// "Application is shutting down", which is the difference between being asked to stop and being killed.
if (Environment.GetEnvironmentVariable("AIKO_LOG_FILE") is { Length: > 0 } logFilePath)
{
    builder.Logging.AddProvider(new FileLoggerProvider(logFilePath));
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ServerJsonContext.Default));
builder.Services.AddHttpContextAccessor();
var dataPaths = AikoDataPaths.FromEnvironment();
builder.Services.AddSingleton(dataPaths);
builder.Services.AddSingleton<AccessTokenStore>();
builder.Services.AddSingleton<DaemonAccessToken>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton<DaemonEndpointConfiguration>();
builder.Services.AddSingleton<AikoDatabase>();
builder.Services.AddSingleton<IProjectCatalog, SqliteProjectCatalog>();
builder.Services.AddSingleton<IDirectoryBrowser, DirectoryBrowser>();
builder.Services.AddSingleton<IWorkshopDiagnostics, WorkshopDoctor>();
builder.Services.AddSingleton<IProjectInitializer, ProjectInitializer>();
builder.Services.AddSingleton<IProjectTemplateStore, FileProjectTemplateStore>();
builder.Services.AddSingleton<IProjectTemplateApplier, ProjectTemplateApplier>();
builder.Services.AddSingleton<IGitClient, GitClient>();
builder.Services.AddSingleton<IProjectAnalytics, SqliteProjectAnalytics>();
builder.Services.AddSingleton<IDaemonTelemetry, SqliteDaemonTelemetry>();
builder.Services.AddSingleton<ICardDiscussionStore, FileCardDiscussionStore>();
builder.Services.AddSingleton<IProjectDefinitionStore, FileProjectDefinitionStore>();
builder.Services.AddSingleton<IProjectGitPolicyReader, FileProjectGitPolicyReader>();
builder.Services.AddSingleton<IProjectLinkStore, FileProjectLinkStore>();
builder.Services.AddSingleton<ICardStore, FileCardStore>();
builder.Services.AddSingleton<ICardBlockers, CardBlockerReader>();
builder.Services.AddSingleton<ICardArtifactStore, FileCardArtifactStore>();
builder.Services.AddSingleton<IRelationStore, FileRelationStore>();
builder.Services.AddSingleton<IMemoryStore, FileMemoryStore>();
builder.Services.AddSingleton<IProjectReindexer, ProjectReindexer>();
builder.Services.AddSingleton<IAppSettingsStore, FileAppSettingsStore>();
builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
builder.Services.AddSingleton<AikoEventBroadcaster>();
builder.Services.AddSingleton<IAikoEventPublisher, SqliteAikoEventPublisher>();
builder.Services.AddSingleton<IAikoEventStore, SqliteAikoEventStore>();
builder.Services.AddSingleton<IExecutionCoordinator, SqliteExecutionCoordinator>();
builder.Services.AddSingleton<IActivityReport, SqliteActivityReport>();
builder.Services.AddSingleton<IAgentAdapter, ClaudeCodeAgentAdapter>();
builder.Services.AddSingleton<IAgentAdapter, CodexAgentAdapter>();
builder.Services.AddSingleton<IAgentAdapter, CursorAgentAdapter>();
builder.Services.AddSingleton<IAgentAdapter, ZCodeAgentAdapter>();
builder.Services.AddSingleton<IAgentAdapter, ClineAgentAdapter>();
builder.Services.AddSingleton<IUnifiedAgentInstaller, UnifiedAgentInstaller>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<ProjectContextTools>()
    .WithTools<CardTools>()
    .WithTools<ExecutionTools>()
    .WithTools<MemoryTools>()
    .WithTools<DaemonTools>()
    .WithTools<MaintenanceTools>()
    // A tool that throws otherwise reaches the agent as "An error occurred invoking 'aiko_start_stage'",
    // with the real cause left in the daemon's log where the agent cannot see it. Handing the message back
    // as the error result is what lets an agent name the reason - an unknown card, a refused start, a
    // constraint the store reported - and either act on it or tell the user, instead of guessing.
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
    {
        try
        {
            return await next(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }]
            };
        }
    }));

var app = builder.Build();

await app.Services.GetRequiredService<AikoDatabase>().InitializeAsync();
// Projects created before slugs existed get their readable handle here, so their URLs become readable
// without anyone re-registering them. A handle already stated in the project's manifest wins, so this is
// idempotent and `aiko repair --fix` performs the same step.
await app.Services.GetRequiredService<IProjectInitializer>().EnsureSlugsAsync(CancellationToken.None);
var configuredUrl = builder.Configuration["AIKO_URL"];
var serverBaseUri = !string.IsNullOrWhiteSpace(configuredUrl)
    ? ValidateExplicitServerUrl(configuredUrl)
    : (await app.Services
        .GetRequiredService<DaemonEndpointConfiguration>()
        .LoadOrCreateAsync(
            ParseRequestedPort(builder.Configuration["AIKO_PORT"]),
            CancellationToken.None))
        .BaseUri;

var insecure = string.Equals(
    builder.Configuration["AIKO_INSECURE"],
    "1",
    StringComparison.OrdinalIgnoreCase);
var accessToken = builder.Configuration["AIKO_TOKEN"]
    ?? await app.Services.GetRequiredService<AccessTokenStore>().GetOrCreateAsync();
// Agent configurations have to carry the token the middleware actually checks, and AIKO_TOKEN can differ
// from what the token file holds, so the resolved value is handed to the writers.
app.Services.GetRequiredService<DaemonAccessToken>().Value = accessToken;
var pairingService = app.Services.GetRequiredService<PairingService>();
if (builder.Configuration["AIKO_PAIR_CODE"] is { } seededPairingCode)
{
    pairingService.Seed(seededPairingCode);
}

if (!insecure)
{
    Console.WriteLine($"Aiko pairing URL: {serverBaseUri}#pair={pairingService.GenerateCode()}");
}

app.UseApiExceptionMapping();

app.Use(async (context, next) =>
{
    if (!IsLoopbackHost(context.Request.Host.Host))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Aiko accepts loopback hosts only.");
        return;
    }

    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrWhiteSpace(origin) &&
        (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
         !originUri.IsLoopback))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Remote browser origins are not allowed.");
        return;
    }

    await next(context);
});

app.UseAikoAuthentication(accessToken, enabled: !insecure);

app.MapStaticAssets();

app.MapSystemEndpoints(serverBaseUri);
app.MapProjectEndpoints();
app.MapBoardEndpoints();
app.MapWorkflowEndpoints();
app.MapCardEndpoints();
app.MapExecutionEndpoints();
app.MapGitEndpoints();
app.MapArtifactEndpoints();
app.MapAgentEndpoints();
app.MapSettingsEndpoints();
app.MapLinkEndpoints();
app.MapEventEndpoints();
app.MapActivityEndpoints();
app.MapFileSystemEndpoints();

app.MapPost(
    "/api/v1/auth/pair",
    (PairRequest request, HttpContext context) =>
    {
        if (!pairingService.TryConsume(request.Code))
        {
            return Results.Unauthorized();
        }

        context.Response.Cookies.Append("aiko_session", accessToken, new CookieOptions
        {
            HttpOnly = true,
            // Deliberately not Secure: the daemon serves plain HTTP on the loopback interface, and a Secure
            // cookie is never sent back over http, which would leave the UI permanently unauthenticated. The
            // loopback binding is the control that keeps this session off any network.
            SameSite = SameSiteMode.Strict,
            IsEssential = true
        });
        return Results.Ok();
    });

app.MapPost(
    "/api/v1/auth/pair-request",
    () => TypedResults.Ok(new PairResponse(pairingService.GenerateCode())));

app.MapMcp("/mcp/projects/{projectId}");
app.MapMcp("/mcp");
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Urls.Add(serverBaseUri.ToString());

// The daemon's own run is recorded before it serves and closed when it stops: a row left open is what
// "crashed last time" looks like, and the daemon screen reports those counts.
var telemetry = app.Services.GetRequiredService<IDaemonTelemetry>();
await telemetry.StartAsync(CancellationToken.None);
app.Lifetime.ApplicationStopping.Register(() =>
    telemetry.StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult());

await app.RunAsync();

static bool IsLoopbackHost(string host) =>
    StringComparer.OrdinalIgnoreCase.Equals(host, "localhost") ||
    StringComparer.Ordinal.Equals(host, "127.0.0.1") ||
    StringComparer.Ordinal.Equals(host, "::1");

static int? ParseRequestedPort(string? configuredPort)
{
    if (string.IsNullOrWhiteSpace(configuredPort))
    {
        return null;
    }

    return int.TryParse(configuredPort, out var port)
        ? port
        : throw new InvalidDataException("AIKO_PORT must be an integer.");
}

static Uri ValidateExplicitServerUrl(string configuredUrl)
{
    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) ||
        !uri.IsLoopback ||
        uri.Scheme != Uri.UriSchemeHttp ||
        !string.IsNullOrEmpty(uri.UserInfo) ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment) ||
        uri.AbsolutePath != "/")
    {
        throw new InvalidDataException(
            "AIKO_URL must be an absolute loopback HTTP origin without a path.");
    }

    return uri;
}
