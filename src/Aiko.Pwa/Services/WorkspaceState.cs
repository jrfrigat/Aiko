using System.Diagnostics;
using System.Net.Http.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Domain.Cards;
using Aiko.Pwa.Contracts;
using Aiko.Pwa.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Aiko.Pwa.Services;

/// <summary>
/// The cockpit's shared workspace: the registered projects, the daemon's identity, the open project's
/// board and settings, and the live SSE subscription.
/// </summary>
/// <remarks>
/// The shell used to own all of this, which is why every screen lived in one component. The screens are
/// separate routes now, so the state sits in a scoped service instead: the rail, the card drawer and
/// whichever page is on screen all read the same snapshot, and nothing has to pass a board down the
/// tree. <see cref="Changed"/> is how a background update (an SSE event, a project switch) reaches the
/// page - the data is already here, the subscriber only re-renders.
/// </remarks>
internal sealed class WorkspaceState : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly ProjectEventClient _events;
    private readonly NavigationManager _navigation;
    private readonly object _sync = new();
    private Task? _initialization;
    private CancellationTokenSource? _boardReloadDelay;
    private bool _disposed;

    /// <summary>
    /// Creates the state over the app's HTTP client, the browser's navigation and its JS runtime.
    /// </summary>
    public WorkspaceState(HttpClient http, NavigationManager navigation, IJSRuntime js)
    {
        _http = http;
        _navigation = navigation;
        _events = new ProjectEventClient(js);
        _events.Received += OnProjectEventAsync;
        _events.ConnectionChanged += OnEventConnectionChangedAsync;
    }

    /// <summary>Registered projects, in the daemon's order.</summary>
    public IReadOnlyList<RegisteredProject> Projects { get; private set; } = [];

    /// <summary>Agent adapters the daemon discovered, with their installations.</summary>
    public IReadOnlyList<AgentAdapterOption> Agents { get; private set; } = [];

    /// <summary>
    /// Which agents the open project has been connected to, as the project's own files report it.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="Agents"/>: that list is machine-wide - the agent is installed and
    /// connected for the user - while this one is about the project, and the two answers differ for a project
    /// nobody has connected an agent to yet.
    /// </remarks>
    public IReadOnlyList<AgentProjectConnection> ProjectConnections { get; private set; } = [];

    /// <summary>
    /// The project templates a new project can be created from. Read once with the rest of the shell data;
    /// the only one that exists today is the built-in default.
    /// </summary>
    public IReadOnlyList<ProjectTemplateSummary> Templates { get; private set; } = [];

    /// <summary>The daemon's own identification, when it answered.</summary>
    public SystemInfo? System { get; private set; }

    /// <summary>
    /// Round-trip time of the daemon's identification call, in milliseconds, or null before that call
    /// completes. The design's top bar reports a live round-trip next to the endpoint; this is the
    /// measured value behind it.
    /// </summary>
    public int? DaemonLatencyMs { get; private set; }

    /// <summary>The open project's board, or null when no project is open.</summary>
    public ProjectBoardSnapshot? Board { get; private set; }

    /// <summary>The open project's effective settings view, or null when no project is open.</summary>
    public AppSettingsView? SettingsView { get; private set; }

    /// <summary>
    /// What the current route carried for the open project: its readable slug, or its id on a link that
    /// still uses one. Both resolve everywhere - the catalog, the REST routes and the MCP route - so the
    /// value is passed through unchanged rather than translated at every call site.
    /// </summary>
    public string? SelectedProjectId { get; private set; }

    /// <summary>The last failure to show, cleared by the next successful load.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether a load is in flight.</summary>
    public bool Loading { get; private set; } = true;

    /// <summary>Whether the open project's SSE stream is connected, as the browser reported it.</summary>
    public bool EventsConnected { get; private set; }

    /// <summary>
    /// Why the SSE stream is not connected, or null while it is. Shown in the shell so a silent stream
    /// reads as a cause rather than as "the board is stale again".
    /// </summary>
    public string? EventsStatusMessage { get; private set; }

    /// <summary>Bumped on every execution event, so the card drawer re-reads its timeline.</summary>
    public int ExecutionPulse { get; private set; }

    /// <summary>Raised whenever the snapshot above changed; subscribers re-render.</summary>
    public event Func<Task>? Changed;

    /// <summary>The open project as registered, or null.</summary>
    public RegisteredProject? SelectedProject => SelectedProjectId is null
        ? null
        : Projects.FirstOrDefault(project =>
            string.Equals(project.Id, SelectedProjectId, StringComparison.Ordinal) ||
            string.Equals(project.Slug, SelectedProjectId, StringComparison.Ordinal));

    /// <summary>The local endpoint the daemon serves, as the top bar's host pill.</summary>
    public string HostCaption => _http.BaseAddress is { } address
        ? $"{address.Host}:{address.Port}"
        : "—";

    /// <summary>
    /// Loads the shell data once - the registered projects, the daemon identity and the agents. Every
    /// other entry point awaits this, so a page can be the first one to ask.
    /// </summary>
    public Task EnsureInitializedAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try
        {
            Projects = await _http.GetFromJsonAsync<IReadOnlyList<RegisteredProject>>(
                "api/v1/projects", PwaJson.Options) ?? [];
            // The top bar's version tag comes from the daemon itself, not from the client build. The
            // stopwatch around the call fills the design's round-trip telemetry with a measured number
            // instead of a mock one.
            var roundTrip = Stopwatch.StartNew();
            System = await _http.GetFromJsonAsync<SystemInfo>("api/v1/system", PwaJson.Options);
            roundTrip.Stop();
            DaemonLatencyMs = (int)roundTrip.ElapsedMilliseconds;
            // The bundle version this tab was loaded with, kept so a later probe can tell that the daemon now
            // serves a different one: the client is then older than the daemon and says so.
            _loadedAssetsVersion ??= System?.AssetsVersion;
            // Which agents the daemon can see is overview information: a failure here must not turn
            // the board into an error screen.
            try
            {
                Agents = await _http.GetFromJsonAsync<IReadOnlyList<AgentAdapterOption>>(
                    "api/v1/agents", PwaJson.Options) ?? [];
            }
            catch (Exception)
            {
                Agents = [];
            }

            // The templates a project can be created from. Same reasoning: the dashboard is still usable
            // without them, and an install that never created a project has no templates root yet.
            await ReloadTemplatesAsync();
        }
        catch (Exception exception)
        {
            Error = FailureText.Describe(exception);
        }
        finally
        {
            Loading = false;
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Opens a project, as addressed by the route. The board is re-read only when the project actually
    /// changed, so moving between its pages keeps the snapshot and the live subscription.
    /// </summary>
    /// <remarks>
    /// A route that names the project by its immutable id is entered again on the handle address, and the
    /// navigation replaces the entry so the back button never returns to an address the UI itself never
    /// builds. The id stays valid for the REST and MCP routes - it is the key every stored file carries -
    /// but it is not an address a person should end up reading.
    /// </remarks>
    public async Task EnsureProjectAsync(string projectId)
    {
        await EnsureInitializedAsync();
        if (ProjectRoutes.ReadableHandle(Projects, projectId) is { } handle &&
            ProjectRoutes.WithHandle(_navigation.Uri, projectId, handle) is { } target)
        {
            _navigation.NavigateTo(target, replace: true);
            return;
        }

        if (string.Equals(SelectedProjectId, projectId, StringComparison.Ordinal))
        {
            return;
        }

        SelectedProjectId = projectId;
        Board = null;
        SettingsView = null;
        Loading = true;
        await NotifyAsync();
        try
        {
            await LoadBoardAsync();
            await LoadProjectConnectionsAsync();
            await ConnectEventsAsync();
        }
        finally
        {
            Loading = false;
            await NotifyAsync();
        }
    }

    /// <summary>
    /// Leaves the open project and returns to the global view: the dashboard has no board, so the
    /// snapshot and the live subscription are released.
    /// </summary>
    public async Task ClearProjectAsync()
    {
        await EnsureInitializedAsync();
        if (SelectedProjectId is null)
        {
            return;
        }

        SelectedProjectId = null;
        Board = null;
        SettingsView = null;
        ProjectConnections = [];
        await _events.CloseAsync();
        EventsConnected = false;
        EventsStatusMessage = null;
        await NotifyAsync();
    }

    /// <summary>
    /// Re-reads the open project's board and effective settings. Does not toggle <see cref="Loading"/>:
    /// callers that want the shell's spinner use <see cref="ReloadAsync"/>.
    /// </summary>
    public async Task LoadBoardAsync()
    {
        Error = null;
        if (SelectedProjectId is null)
        {
            Board = null;
            await NotifyAsync();
            return;
        }

        try
        {
            var projectId = Uri.EscapeDataString(SelectedProjectId);
            Board = await _http.GetFromJsonAsync<ProjectBoardSnapshot>(
                $"api/v1/projects/{projectId}/board", PwaJson.Options);
            SettingsView = await _http.GetFromJsonAsync<AppSettingsView>(
                $"api/v1/projects/{projectId}/settings", PwaJson.Options);
        }
        catch (Exception exception)
        {
            Error = FailureText.Describe(exception);
        }

        await NotifyAsync();
    }

    /// <summary>Re-reads the shell and the open board, showing the shell's spinner while it runs.</summary>
    public async Task ReloadAsync()
    {
        Loading = true;
        await NotifyAsync();
        try
        {
            await ReloadProjectsAsync();
            await ReloadAgentsAsync();

            await LoadBoardAsync();
        }
        finally
        {
            Loading = false;
            await NotifyAsync();
        }
    }

    /// <summary>
    /// Registers a project from a path, reloads the project list and opens the new project.
    /// </summary>
    /// <returns>The registered project, or null when the daemon rejected it.</returns>
    public async Task<RegisteredProject?> InitializeProjectAsync(
        string rootPath,
        string? name,
        ProjectGitPolicy gitPolicy,
        string? templateId = null,
        string? slug = null,
        IReadOnlyList<string>? agentAdapterIds = null)
    {
        await EnsureInitializedAsync();
        Error = null;
        try
        {
            using var response = await _http.PostAsJsonAsync(
                "api/v1/projects/initialize",
                new InitializeProjectRequest(rootPath, name, gitPolicy, templateId, slug),
                PwaJson.Options);
            if (!response.IsSuccessStatusCode)
            {
                // A project id that is already taken comes back as a conflict carrying its own sentence.
                // Replacing it with the status code would hide the one thing the user has to change.
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(PwaJson.Options);
                Error = error?.Message ?? Loc.Format("ServerRejectedChanges", (int)response.StatusCode);
                return null;
            }

            var project = await response.Content.ReadFromJsonAsync<RegisteredProject>(PwaJson.Options);
            await ReloadProjectsAsync();
            // The first project writes the installation's default template, so the list the add-project
            // form offers is refreshed here rather than at the next page load.
            await ReloadTemplatesAsync();
            if (project is not null && agentAdapterIds is { Count: > 0 })
            {
                // The form asked for these agents, so they are connected in the same gesture: the project is
                // created and connected rather than created and then needing a second step the user must know.
                await ConnectProjectAgentsAsync(project.Id, agentAdapterIds);
            }

            return project;
        }
        catch (Exception exception)
        {
            Error = Loc.Format("AddProjectFailed", FailureText.Describe(exception));
            await NotifyAsync();
            return null;
        }
    }
    /// <summary>
    /// Re-reads the project templates. A failure is not an error state: an installation that never created
    /// a project has no templates root yet, and the daemon writes the default template at the first init.
    /// </summary>
    public async Task ReloadTemplatesAsync()
    {
        try
        {
            Templates = await _http.GetFromJsonAsync<IReadOnlyList<ProjectTemplateSummary>>(
                "api/v1/templates", PwaJson.Options) ?? [];
        }
        catch (Exception)
        {
            Templates = [];
        }
    }

    /// <summary>
    /// Re-reads the agent adapters: whether each agent is installed on this machine and whether Aiko has
    /// connected to it.
    /// </summary>
    /// <summary>
    /// Connects the chosen agents to a project.
    /// </summary>
    /// <remarks>
    /// The same call the CLI makes, and idempotent on the daemon side: an agent that is already connected has
    /// its files rewritten to the same content rather than being reported as a failure, so a user can tick an
    /// agent without first having to know whether it was connected already.
    /// </remarks>
    /// <param name="projectId">Identifier of the project, by id or by handle.</param>
    /// <param name="adapterIds">Adapter ids to connect.</param>
    public async Task ConnectProjectAgentsAsync(string projectId, IReadOnlyList<string> adapterIds)
    {
        if (adapterIds.Count == 0)
        {
            return;
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"api/v1/projects/{Uri.EscapeDataString(projectId)}/installation",
                new ConnectProjectAgentsRequest(adapterIds),
                PwaJson.Options);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
        {
            Error = Loc.Format("AddProjectFailed", FailureText.Describe(exception));
            await NotifyAsync();
        }
    }

    /// <summary>
    /// Reads which agents are connected to the open project.
    /// </summary>
    /// <remarks>
    /// A call of its own rather than part of the board: the project's files decide the answer, and they change
    /// when an agent is connected or a file is deleted by hand, not when a card moves. Like the agent list,
    /// this is overview information, so a failure leaves an empty list instead of an error screen.
    /// </remarks>
    public async Task LoadProjectConnectionsAsync()
    {
        if (SelectedProjectId is not { Length: > 0 } projectId)
        {
            ProjectConnections = [];
            return;
        }

        try
        {
            ProjectConnections = await _http.GetFromJsonAsync<IReadOnlyList<AgentProjectConnection>>(
                $"api/v1/projects/{Uri.EscapeDataString(projectId)}/installation",
                PwaJson.Options) ?? [];
        }
        catch (Exception)
        {
            ProjectConnections = [];
        }
    }

    public async Task ReloadAgentsAsync()
    {
        try
        {
            Agents = await _http.GetFromJsonAsync<IReadOnlyList<AgentAdapterOption>>(
                "api/v1/agents", PwaJson.Options) ?? [];
        }
        catch (Exception exception)
        {
            Error = FailureText.Describe(exception);
        }

        await NotifyAsync();
    }



    /// <summary>Re-reads the registered project list.</summary>
    public async Task ReloadProjectsAsync()
    {
        try
        {
            Projects = await _http.GetFromJsonAsync<IReadOnlyList<RegisteredProject>>(
                "api/v1/projects", PwaJson.Options) ?? [];
        }
        catch (Exception exception)
        {
            Error = FailureText.Describe(exception);
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Connects Aiko to one agent on this machine: writes its global <c>/aiko-*</c> skills and commands,
    /// then applies the state the daemon reports back so the card cannot show something older than the
    /// click that was just made.
    /// </summary>
    public Task<bool> ConnectAgentAsync(string adapterId) =>
        SendAgentCommandAsync(HttpMethod.Post, adapterId);

    /// <summary>Removes Aiko's global configuration for one agent, leaving the agent itself alone.</summary>
    public Task<bool> DisconnectAgentAsync(string adapterId) =>
        SendAgentCommandAsync(HttpMethod.Delete, adapterId);

    private async Task<bool> SendAgentCommandAsync(HttpMethod method, string adapterId)
    {
        try
        {
            using var request = new HttpRequestMessage(
                method,
                $"api/v1/agents/{Uri.EscapeDataString(adapterId)}/installation");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Error = Loc.Format("AgentConnectFailed", adapterId, (int)response.StatusCode);
                await NotifyAsync();
                return false;
            }

            if (await response.Content.ReadFromJsonAsync<AgentConnectionResponse>(PwaJson.Options) is { } answer)
            {
                Agents = Agents
                    .Select(adapter => StringComparer.Ordinal.Equals(adapter.Id, adapterId)
                        ? answer.Adapter
                        : adapter)
                    .ToArray();
            }

            Error = null;
            await NotifyAsync();
            return true;
        }
        catch (Exception exception)
        {
            Error = Loc.Format("AgentConnectFailed", adapterId, FailureText.Describe(exception));
            await NotifyAsync();
            return false;
        }
    }


    /// <summary>Clears the last error.</summary>
    public async Task ClearErrorAsync()
    {
        if (Error is null)
        {
            return;
        }

        Error = null;
        await NotifyAsync();
    }

    /// <summary>Reports a page-level failure through the shell's error banner.</summary>
    public async Task ReportErrorAsync(string message)
    {
        Error = message;
        await NotifyAsync();
    }

    private async Task ConnectEventsAsync()
    {
        if (SelectedProjectId is null || _http.BaseAddress is null)
        {
            return;
        }

        EventsStatusMessage = null;
        try
        {
            await _events.ConnectAsync(_http.BaseAddress.ToString(), SelectedProjectId);
            // The outcome is not known yet: EventSource connects asynchronously, and the browser reports
            // both the open stream and the failure through OnEventConnectionChangedAsync. Claiming a live
            // stream here is what made the shell's badge say "SSE LIVE" over a stream that never opened.
        }
        catch (JSDisconnectedException exception)
        {
            EventsConnected = false;
            EventsStatusMessage = exception.Message;
        }
        catch (JSException exception)
        {
            EventsConnected = false;
            EventsStatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            // JS interop is unavailable (prerender): the board still works, just without live events.
            EventsConnected = false;
            EventsStatusMessage = exception.Message;
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Whether the client that is running is older than the bundle the daemon now serves.
    /// </summary>
    /// <remarks>
    /// Both sides must state a version for the answer to mean anything: a source run that serves no built
    /// bundle says nothing, and "unknown" is not the same claim as "stale".
    /// </remarks>
    /// <param name="loadedAssetsVersion">The bundle version this tab was loaded with.</param>
    /// <param name="daemonAssetsVersion">The bundle version the daemon serves now.</param>
    internal static bool IsClientStale(string? loadedAssetsVersion, string? daemonAssetsVersion) =>
        loadedAssetsVersion is { Length: > 0 } &&
        daemonAssetsVersion is { Length: > 0 } &&
        !StringComparer.Ordinal.Equals(loadedAssetsVersion, daemonAssetsVersion);

    /// <summary>
    /// Applies the stream's state as the browser reports it: the shell's live badge follows a real open
    /// connection, and a failure carries the reason instead of leaving the board quietly stale.
    /// </summary>
    private async Task OnEventConnectionChangedAsync(bool connected, string? reason)
    {
        EventsConnected = connected;
        EventsStatusMessage = connected ? null : reason;
        if (connected)
        {
            // A (re)connect is how a daemon restart shows up here - and a restart is exactly when the daemon
            // may serve a newer client than this tab loaded. Probing the version at that moment is what lets
            // the shell say "reload" instead of hiding the mismatch under a live badge.
            await RefreshSystemAsync();
        }

        await NotifyAsync();
    }

    /// <summary>
    /// Re-reads the daemon identity and raises <see cref="UpdateAvailable"/> when the served bundle changed.
    /// </summary>
    private async Task RefreshSystemAsync()
    {
        try
        {
            var roundTrip = Stopwatch.StartNew();
            var system = await _http.GetFromJsonAsync<SystemInfo>("api/v1/system", PwaJson.Options);
            roundTrip.Stop();
            if (system is null)
            {
                return;
            }

            DaemonLatencyMs = (int)roundTrip.ElapsedMilliseconds;
            System = system;
            if (IsClientStale(_loadedAssetsVersion, system.AssetsVersion))
            {
                UpdateAvailable = true;
            }
        }
        catch (Exception)
        {
            // A failed probe must not turn the board into an error screen; the next reconnect tries again.
        }
    }

    private string? _loadedAssetsVersion;

    /// <summary>
    /// Whether the daemon serves a newer client than the one this tab loaded, so reloading is worth it.
    /// </summary>
    public bool UpdateAvailable { get; private set; }

    private async Task OnProjectEventAsync(string projectId, AikoEvent @event)
    {
        // Events are journaled and broadcast under the project's immutable id, while the route may have
        // opened the project by its readable handle. Comparing those two strings directly dropped every
        // event of a project addressed by its handle - which is exactly the URL the UI hands out - so the
        // board and the card page only moved when the page was reloaded by hand.
        if (!BelongsToOpenProject(projectId, SelectedProject))
        {
            return;
        }

        switch (@event.Type)
        {
            case AikoEventTypes.CardUpdated:
            case AikoEventTypes.RelationsUpdated:
                ScheduleBoardReload();
                break;
            case AikoEventTypes.ExecutionUpdated:
                ExecutionPulse++;
                await NotifyAsync();
                break;
        }
    }

    /// <summary>
    /// Whether an event received for <paramref name="eventProjectId"/> concerns the open project.
    /// </summary>
    /// <remarks>
    /// The comparison is deliberately made against the project's id and not against the value the route
    /// carried: the route value is a handle for people to read, and only the id is what the daemon's
    /// events carry.
    /// </remarks>
    internal static bool BelongsToOpenProject(string eventProjectId, RegisteredProject? openProject) =>
        openProject is not null &&
        StringComparer.Ordinal.Equals(eventProjectId, openProject.Id);

    /// <summary>
    /// Coalesces a burst of board events into one reload: an agent finishing a stage publishes several
    /// card updates in a row, and each one would otherwise re-read the whole board.
    /// </summary>
    private void ScheduleBoardReload()
    {
        CancellationToken token;
        lock (_sync)
        {
            _boardReloadDelay?.Cancel();
            _boardReloadDelay?.Dispose();
            _boardReloadDelay = new CancellationTokenSource();
            token = _boardReloadDelay.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token);
                if (!token.IsCancellationRequested)
                {
                    await LoadBoardAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // A newer event rescheduled the reload.
            }
        });
    }

    private async Task NotifyAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (Changed is { } handler)
        {
            await handler();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _events.Received -= OnProjectEventAsync;
        _events.ConnectionChanged -= OnEventConnectionChangedAsync;
        lock (_sync)
        {
            _boardReloadDelay?.Cancel();
            _boardReloadDelay?.Dispose();
            _boardReloadDelay = null;
        }

        await _events.DisposeAsync();
    }
}
