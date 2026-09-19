using Aiko.Pwa.Services;
using Xunit;

namespace Aiko.Pwa.Specs;

/// <summary>
/// Guards that an updated daemon can actually reach the browser, and that a client left behind says so.
/// </summary>
/// <remarks>
/// The bug behind this: the installed daemon is a released build, so a fix in the working copy never reached
/// it, and the published client kept the app shell in a cache-first service worker - so a reload still served
/// the old client. The board looked live (the badge is drawn by the shell) while every event was ignored.
/// </remarks>
public sealed class ClientFreshnessSpecs
{
    [Fact]
    public void A_new_service_worker_takes_over_and_asks_the_daemon_for_the_shell()
    {
        var js = Text("src", "Aiko.Pwa", "wwwroot", "service-worker.published.js");

        // A waiting worker is what kept the old client alive until every tab was closed ...
        Assert.Contains("self.skipWaiting()", js, StringComparison.Ordinal);
        Assert.Contains("self.clients.claim()", js, StringComparison.Ordinal);

        // ... and the shell is fetched before the cache is consulted, so an updated daemon is picked up by the
        // next reload instead of being answered from a cache that still holds the previous client.
        Assert.Contains("event.request.mode === 'navigate'", js, StringComparison.Ordinal);
        Assert.Contains("/_framework/blazor.webassembly.js", js, StringComparison.Ordinal);
    }

    [Fact]
    public void A_client_older_than_the_served_bundle_is_called_stale()
    {
        Assert.True(WorkspaceState.IsClientStale("A", "B"));
        Assert.False(WorkspaceState.IsClientStale("A", "A"));

        // Nothing to compare is not a claim: a source run serves no bundle, and "unknown" is not "stale".
        Assert.False(WorkspaceState.IsClientStale(null, "B"));
        Assert.False(WorkspaceState.IsClientStale("A", null));
        Assert.False(WorkspaceState.IsClientStale(string.Empty, string.Empty));
    }

    [Fact]
    public void The_shell_offers_a_reload_when_the_client_is_stale()
    {
        var text = Text("src", "Aiko.Pwa", "Layout", "MainLayout.razor");
        Assert.Contains("@if (State.UpdateAvailable)", text, StringComparison.Ordinal);
        Assert.Contains("Loc.Get(\"UpdateAvailable\")", text, StringComparison.Ordinal);
        Assert.Contains("ReloadForUpdate", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_daemon_reports_the_bundle_version_it_serves()
    {
        var text = Text("src", "Aiko.Server", "Endpoints", "SystemEndpoints.cs");
        Assert.Contains("service-worker-assets.js", text, StringComparison.Ordinal);
        Assert.Contains("AssetsVersion(environment)", text, StringComparison.Ordinal);
    }

    private static string Text(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    /// <summary>Walks up from this assembly to the solution file, as the other specs do.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(ClientFreshnessSpecs).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiko.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Aiko repository root.");
    }
}
