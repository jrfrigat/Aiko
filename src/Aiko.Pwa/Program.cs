using System.Globalization;
using Aiko.Theme.StitchFlow;
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
    options.RegisterAllBuiltInThemes = false;
});
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
