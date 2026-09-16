using System.Text.Json;
using Aiko.Application.Contracts;
using Microsoft.JSInterop;

namespace Aiko.Pwa.Services;

/// <summary>
/// Live subscription to the daemon's per-project SSE stream through the browser
/// EventSource. EventSource reconnects automatically and replays missed events
/// via the Last-Event-Id header.
/// </summary>
public sealed class ProjectEventClient(IJSRuntime js) : IAsyncDisposable
{
    private DotNetObjectReference<ProjectEventClient>? _reference;
    private IJSObjectReference? _source;
    private string? _subscribedProjectId;

    /// <summary>
    /// Raised for every received project event on the UI synchronization context.
    /// </summary>
    public event Func<string, AikoEvent, Task>? Received;

    /// <summary>
    /// Whether an EventSource connection is currently open.
    /// </summary>
    public bool IsConnected => _source is not null;

    /// <summary>
    /// Connects to the event stream of a project, replacing any previous subscription.
    /// </summary>
    public async Task ConnectAsync(string baseUrl, string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        if (_subscribedProjectId == projectId && _source is not null)
        {
            return;
        }

        await CloseAsync();
        _reference ??= DotNetObjectReference.Create(this);
        var url = $"{baseUrl.TrimEnd('/')}/api/v1/projects/{Uri.EscapeDataString(projectId)}/events";
        _source = await js.InvokeAsync<IJSObjectReference>("aikoEvents.connect", url, _reference);
        _subscribedProjectId = projectId;
    }

    /// <summary>
    /// Closes the current subscription, if any.
    /// </summary>
    public async Task CloseAsync()
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("aikoEvents.close", _source);
        }
        catch (JSDisconnectedException)
        {
            // The Blazor circuit is already gone.
        }

        _source = null;
        _subscribedProjectId = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _reference?.Dispose();
    }

    [JSInvokable]
    public async Task OnEvent(string json)
    {
        AikoEvent? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AikoEvent>(json, PwaJson.Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (parsed is null)
        {
            return;
        }

        var handler = Received;
        if (handler is not null)
        {
            await handler(parsed.ProjectId, parsed);
        }
    }
}
