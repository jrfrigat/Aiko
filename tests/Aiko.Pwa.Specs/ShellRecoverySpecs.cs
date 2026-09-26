using System.Net;
using System.Text;
using System.Text.Json;
using Aiko.Application.Agents;
using Aiko.Application.Contracts;
using Aiko.Pwa.Contracts;
using Aiko.Pwa.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards what a tab does when its first load met a daemon that was not running yet.
/// </summary>
/// <remarks>
/// The bug this pins: the shell is read once and nothing read it again. The live stream only connects once a
/// project is selected, so a reconnect could never bring the project list back - and the returning tab took the
/// plate away while the shell stayed empty, which is worse than the message it replaced.
/// </remarks>
public sealed class ShellRecoverySpecs
{
    private static readonly RegisteredProject Project = new(
        "01a0aec7627476cb9e622df271b9b20f",
        "Aiko",
        @"C:\Projects\Aiko",
        "aiko");

    [Fact]
    public async Task A_tab_that_met_a_daemon_that_was_down_says_so_and_has_kept_its_empty_shell()
    {
        var daemon = new StubDaemon { Running = false };
        var state = CreateState(daemon);

        await state.EnsureInitializedAsync();

        Assert.Empty(state.Projects);
        Assert.False(string.IsNullOrEmpty(state.Error));
    }

    [Fact]
    public async Task A_returning_tab_reads_the_shell_again_and_the_plate_goes_with_the_read()
    {
        var daemon = new StubDaemon { Running = false };
        var state = CreateState(daemon);
        await state.EnsureInitializedAsync();

        // The daemon comes up while the tab is elsewhere: the person minimizes and comes back.
        daemon.Running = true;
        await state.OnVisibilityChanged(visible: false);
        await state.OnVisibilityChanged(visible: true);

        Assert.Single(state.Projects);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task The_plate_stays_while_the_daemon_is_still_down()
    {
        var daemon = new StubDaemon { Running = false };
        var state = CreateState(daemon);
        await state.EnsureInitializedAsync();

        await state.OnVisibilityChanged(visible: false);
        await state.OnVisibilityChanged(visible: true);

        // Nothing was read, so nothing may have been taken away either.
        Assert.Empty(state.Projects);
        Assert.False(string.IsNullOrEmpty(state.Error));
    }

    /// <summary>The client state under test, over a stub daemon and with no browser at all.</summary>
    private static WorkspaceState CreateState(StubDaemon daemon) => new(
        new HttpClient(daemon) { BaseAddress = new Uri("http://localhost:5180/") },
        new StubNavigation(),
        new StubJs());

    /// <summary>
    /// A daemon that answers the shell endpoints and can be switched into the state a stopped daemon is in.
    /// </summary>
    private sealed class StubDaemon : HttpMessageHandler
    {
        /// <summary>Whether the daemon is up. A request to a daemon that is not answers 503, as a stopped one does.</summary>
        public bool Running { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!Running)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            // The bodies are built from the client's own types and options, so a shape the client cannot read
            // fails here for the same reason it would fail against the daemon.
            var path = request.RequestUri!.AbsolutePath;
            IReadOnlyList<RegisteredProject> projects = [Project];
            var body = path switch
            {
                "/api/v1/projects" => JsonSerializer.Serialize(projects, PwaJson.Options),
                "/api/v1/system" => JsonSerializer.Serialize(Identity(), PwaJson.Options),
                _ => "[]",
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>The daemon's own identification, as the top bar reads it.</summary>
        private static SystemInfo Identity() => new(
            "Aiko",
            "0.0.0",
            "net10.0",
            Environment.ProcessId,
            "http://localhost:5180/",
            DateTimeOffset.UnixEpoch,
            new DaemonTelemetry(DateTimeOffset.UnixEpoch, TimeSpan.Zero, 0, 0, 0, 0),
            AssetsVersion: null);
    }

    /// <summary>A navigation manager that goes nowhere, which is what the shell needs before its first route.</summary>
    private sealed class StubNavigation : NavigationManager
    {
        public StubNavigation() => Initialize("http://localhost:5180/", "http://localhost:5180/");
    }

    /// <summary>No browser: interop fails the way it does when there is no JS runtime, and the client says so.</summary>
    private sealed class StubJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSDisconnectedException("There is no browser in a spec.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            throw new JSDisconnectedException("There is no browser in a spec.");
    }
}
