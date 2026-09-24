using System.Globalization;
using Aiko.Theme.StitchFlow;
using Flare.Abstractions.Tokens;
using Flare.Extensions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Aiko.Pwa;
using Aiko.Pwa.Resources;
using Aiko.Pwa.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFlare(options =>
{
    // The app's own theme package: the cockpit design language with the Aiko palette. No built-in
    // theme is registered, so nothing can fall back to Material's look at runtime.
    options.DefaultTheme = StitchFlowTheme.Create();
    // Three places have to agree on the mode a fresh visitor gets, and all three are needed: this
    // default, the data-default-mode attribute the bootstrap script paints the first frame from, and
    // the provider's RespectSystemColorScheme in App.razor - without that last one nothing reads the
    // system's preference, and "auto" has nothing to resolve against. Two of them disagreed once, and
    // a fresh visitor got the bootstrap's dark frame with a light theme painted over it.
    // "Auto" is the default because this is a workspace on someone's machine, not a brand: it should
    // look like the rest of the machine until the person says otherwise.
    options.DefaultMode = ThemeMode.Auto;
    options.RegisterAllBuiltInThemes = false;
});
// X-Aiko-Request says a write comes from the daemon's own page: the daemon refuses a write its session
// cookie authenticates without it, because a form on another local page could send one.
builder.Services.AddScoped(_ =>
{
    var client = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    client.DefaultRequestHeaders.Add("X-Aiko-Request", "1");
    return client;
});
// The cockpit's shared workspace: one snapshot of projects, board and live events per browser session,
// read by the shell and by every page.
builder.Services.AddScoped<WorkspaceState>();

var host = builder.Build();
// The language has to be settled before the first component renders, so the host is built first and
// run afterwards.
await ApplyLanguageAsync(host);
await host.RunAsync();

// Settles the UI language before the first frame renders: the language chosen in the interface first,
// then the browser's ordered preferences, then the neutral (English) resources.
//
// The saved choice is read from the same local-storage key aiko-i18n.js reads, because that script
// settles the document language and Blazor's error bar on the first frame while this settles the
// culture every component renders in. Reading one stored value is what keeps the two from disagreeing
// - the dark frame under a light theme that a single mismatched default-mode once produced came from
// exactly this kind of split, and a language that resolves differently in the two places is the same
// fault with the UI showing it for the whole session instead of one frame.
static async Task ApplyLanguageAsync(WebAssemblyHost host)
{
    string? saved = null;
    string[] preferred;
    try
    {
        var js = host.Services.GetRequiredService<IJSRuntime>();

        // A stale service-worker cache can still be serving a script that knows only the browser's
        // languages: a missing function must cost the saved choice, not the browser's preference.
        try
        {
            saved = await js.InvokeAsync<string>("aikoSavedLanguage");
        }
        catch (JSException)
        {
        }

        var published = await js.InvokeAsync<string>("aikoLanguages");
        preferred = published?.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    }
    catch (JSException)
    {
        // No JS runtime (prerender, or JS disabled): fall through to the neutral culture.
        preferred = [];
    }
    catch (InvalidOperationException)
    {
        preferred = [];
    }

    var culture = UiLanguages.Resolve(preferred, saved);
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

