using Ahnen.Core.Services;
using Ahnen.Web.Components;
using Ahnen.Web.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ApplicationLogBuffer>();
builder.Services.AddSingleton<ILoggerProvider, ApplicationLogProvider>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddSingleton(sp => WebDataStoreOptions.Create(
    sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<RelationshipService>();
builder.Services.AddSingleton<ChartDataService>();
builder.Services.AddSingleton<PhotoService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

var supportedCultures = new[] { "en", "de" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

app.UseAntiforgery();

app.MapStaticAssets();

// Serve photos (and only photos) from the data directory.
var store = app.Services.GetRequiredService<DataStore>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(store.PhotosDirectory),
    RequestPath = "/media/photos"
});

// Language switcher: sets the culture cookie and reloads the page.
app.MapGet("/set-culture", (string culture, string? redirect, HttpContext http) =>
{
    if (supportedCultures.Contains(culture))
    {
        http.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
    }
    var target = redirect is { Length: > 0 } && redirect.StartsWith('/') ? redirect : "/";
    return Results.LocalRedirect(target);
});

app.MapRazorComponents<Ahnen.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
