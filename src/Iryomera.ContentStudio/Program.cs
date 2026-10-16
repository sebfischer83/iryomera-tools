using System.Globalization;
using Iryomera.Content.SDK;
using Iryomera.ContentStudio.Components;
using Iryomera.ContentStudio.Data;
using Iryomera.ContentStudio.Services;
using Iryomera.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5180");
var dataPath = Path.GetFullPath(builder.Configuration["Studio:DataDirectory"] ?? Path.Combine(Environment.CurrentDirectory, ".iryomera", "studio"));
Directory.CreateDirectory(dataPath);
builder.Services.AddDbContextFactory<StudioDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(dataPath, "studio.db") + ";Default Timeout=15"));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<ProjectService>(); builder.Services.AddScoped<ImportService>(); builder.Services.AddScoped<StudioExportService>();
builder.Services.AddSingleton(StudioUiCatalogs.Load());
builder.Services.AddSingleton<IStringLocalizer>(service => new StudioStringLocalizer(service.GetRequiredService<ContentLocalizer>()));
builder.Services.Configure<RequestLocalizationOptions>(options => options.SetDefaultCulture("en").AddSupportedCultures("en", "de").AddSupportedUICultures("en", "de"));
builder.Configuration["AllowedHosts"] ??= "localhost;127.0.0.1;[::1]";
var app = builder.Build();
app.UseRequestLocalization();
app.UseExceptionHandler("/error");
app.UseStaticFiles(); app.UseAntiforgery();
app.MapStaticAssets();
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<StudioDbContext>>().CreateDbContextAsync())
{ await db.Database.MigrateAsync(); await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;"); }
app.MapGet("/culture", (HttpContext context, string culture, string? returnUrl) =>
{
    if (culture is not ("en" or "de")) return Results.BadRequest();
    context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new(culture)),
        new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = TimeSpan.FromDays(365) });
    var destination = returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.Contains('\\') ? returnUrl : "/";
    return Results.LocalRedirect(destination);
});
app.MapGet("/download/{id:guid}", async (Guid id, StudioExportService exports, IStringLocalizer text, CancellationToken ct) =>
{
    try { return Results.File(await exports.ExportAsync(id, ct), "application/zip", "iryomera-content-" + id.ToString("N") + ".zip"); }
    catch (Exception e) when (e is InvalidOperationException or StudioValidationException or ArgumentException)
    { return Results.Problem(text["studio.exportBlocked"] + "\n" + e.Message, statusCode: 422); }
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;

namespace Iryomera.ContentStudio.Services
{
    public static class StudioUiCatalogs
    {
        public static ContentLocalizer Load()
        {
            var catalogs = new List<LocalizationDefinition>();
            foreach (var language in new[] { "en", "de" })
            {
                using var stream = typeof(StudioUiCatalogs).Assembly.GetManifestResourceStream("Iryomera.ContentStudio.Resources." + language + ".json") ?? throw new InvalidOperationException("Missing UI catalog.");
                catalogs.Add(System.Text.Json.JsonSerializer.Deserialize<LocalizationDefinition>(stream, ContentJson.Options) ?? throw new InvalidDataException("Invalid UI catalog."));
            }
            return new(catalogs);
        }
    }
}
