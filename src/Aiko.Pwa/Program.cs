using System.Globalization;
using Aiko.Theme.StitchFlow;
using Flare.Abstractions.Tokens;
using Flare.Extensions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Aiko.Pwa;

var culture = new CultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

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

await builder.Build().RunAsync();
