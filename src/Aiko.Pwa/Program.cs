using System.Globalization;
using Aiko.Theme.StitchFlow;
using Flare.Abstractions.Tokens;
using Flare.Extensions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Aiko.Pwa;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFlare(options =>
{
    // The app's own theme package: the cockpit design language with the Aiko palette. No built-in
    // theme is registered, so nothing can fall back to Material's look at runtime.
    options.DefaultTheme = StitchFlowTheme.Create();
    // The design is dark-only, and this must agree with data-default-mode in index.html: the script
    // paints the first frame from the attribute, and once .NET boots the service applies the mode it
    // was configured with. Leaving it unset made the two disagree, so a fresh visitor got the
    // bootstrap's dark frame and then a light theme painted over it.
    options.DefaultMode = ThemeMode.Dark;
    options.RegisterAllBuiltInThemes = false;
});
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

var host = builder.Build();
// The language has to be settled before the first component renders, so the host is built first and
// run afterwards.
await ApplyBrowserLanguageAsync(host);
await host.RunAsync();

// Chooses the UI culture from the browser's language preferences - the app does not pin one. The
// neutral resources are English, so a browser language we do not translate resolves through them
// instead of showing resource keys, while Russian resolves to the satellite resources we ship. The
// preference list is honoured in order, which is what the platform asks for: a visitor who lists
// Ukrainian, Russian and English gets the first of those we can serve.
static async Task ApplyBrowserLanguageAsync(WebAssemblyHost host)
{
    string[] preferred;
    try
    {
        var js = host.Services.GetRequiredService<IJSRuntime>();
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

    var culture = ResolveCulture(preferred);
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

// The first browser language the runtime can name, or the invariant culture - which resource lookup
// treats as "use the neutral resources", i.e. English.
static CultureInfo ResolveCulture(IReadOnlyList<string> preferred)
{
    foreach (var language in preferred)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            // A tag the ICU data does not know (or a malformed one): try the next preference.
        }
    }

    return CultureInfo.InvariantCulture;
}
