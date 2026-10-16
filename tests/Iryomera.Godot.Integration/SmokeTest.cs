using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using Godot;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Iryomera.ModIo;
using Iryomera.ModIo.ModKit;
using HttpClient = System.Net.Http.HttpClient;

public partial class SmokeTest : Node
{
    public override async void _Ready()
    {
        try
        {
            Assert(typeof(ContentPackage).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName == ".NETCoreApp,Version=v8.0", "SDK net8.0 package asset was not selected.");
            Assert(typeof(ContentLocalizer).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName == ".NETCoreApp,Version=v8.0", "Localization net8.0 package asset was not selected.");
            Assert(System.Environment.Version.Major == 8, "The game smoke test must execute on the actual .NET 8 runtime.");
            await using var source = File.OpenRead(ProjectSettings.GlobalizePath("res://annotations.json"));
            var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(source);
            Assert(result.IsValid, string.Join("; ", result.Issues));
            var package = result.Value!;
            Assert(package.MedicalAnnotations.Count == 2 && package.Relations.Count == 0, "Medical annotation context was projected or lost.");
            Assert(package.MedicalAnnotations[0].IsNegated == true && package.MedicalAnnotations[0].Frequency!.SourceValue == "0/5", "Source negation/frequency changed.");
            await using var roundTrip = new MemoryStream(); await ContentJson.WriteAsync(roundTrip, package); roundTrip.Position = 0;
            var copy = await ContentJson.ReadAsync<ContentPackage>(roundTrip);
            Assert(JsonSerializer.Serialize(package, ContentJson.Options) == JsonSerializer.Serialize(copy, ContentJson.Options), "JSON round trip changed source context.");
            var catalogs = new List<LocalizationDefinition>();
            foreach (var language in new[] { "en", "de" })
            {
                await using var input = File.OpenRead(ProjectSettings.GlobalizePath("res://" + language + ".json"));
                catalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(input));
            }
            var localizer = new ContentLocalizer(catalogs);
            var key = "synthetic.disease.amber_syndrome.name";
            Assert(localizer.Format(key, CultureInfo.GetCultureInfo("en")) == "Synthetic Amber syndrome", "English rendering failed.");
            Assert(localizer.Format(key, CultureInfo.GetCultureInfo("de")) == "Synthetisches Bernsteinsyndrom", "German rendering failed.");
            Assert(localizer.Format("synthetic.ui.patients", CultureInfo.GetCultureInfo("de"), new Dictionary<string, object?> { ["count"] = 2 }) == "2 Demo-Patienten", "Plural formatting failed.");
            await using var diagnosticSource = File.OpenRead(ProjectSettings.GlobalizePath("res://diagnostics.json"));
            var diagnostic = await ContentDocuments.ReadValidatedAsync<ContentPackage>(diagnosticSource);
            Assert(diagnostic.IsValid, string.Join("; ", diagnostic.Issues));
            Assert(diagnostic.Value!.Definitions.OfType<ExaminationDefinition>().Count() == 5, "Shared examination definitions were lost.");
            Assert(diagnostic.Value.Definitions.OfType<ObservationDefinition>().Select(o => o.Result!.ValueKind).Distinct().Count() == 4, "Typed observation results were lost.");
            Assert(diagnostic.Value.DiagnosticSimulation!.ObservationDistributions.Count == 15, "Simulation configuration was lost.");
            var diagnosticCatalogs = new List<LocalizationDefinition>();
            foreach (var language in new[] { "en", "de" })
            {
                await using var input = File.OpenRead(ProjectSettings.GlobalizePath("res://diagnostics." + language + ".json"));
                diagnosticCatalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(input));
            }
            Assert(LocalizationValidator.Validate(diagnosticCatalogs, LocalizationValidator.GetContentKeys(diagnostic.Value), ["en", "de"]).Count == 0, "Diagnostic DE/EN catalogs do not match.");
            await using var modSource = File.OpenRead(ProjectSettings.GlobalizePath("res://mod-content.json"));
            var modContent = await ContentDocuments.ReadValidatedAsync<ContentPackage>(modSource);
            Assert(modContent.IsValid, string.Join("; ", modContent.Issues));
            var furniture = modContent.Value!.Definitions.OfType<FurnitureDefinition>().Single();
            Assert(furniture.Assets!.Count == 3 && furniture.Placement!.Footprint.Width == 2 && furniture.Placement.InteractionPoints.Count == 1, "Portable asset/placement contracts were lost.");
            var modCatalogs = new List<LocalizationDefinition>();
            foreach (var language in new[] { "en", "de" })
            {
                await using var input = File.OpenRead(ProjectSettings.GlobalizePath("res://mod-" + language + ".json"));
                modCatalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(input));
            }
            Assert(ContentLocalizationValidator.Validate(modContent.Value, modCatalogs, ["example_mod.mod.name"]).Count == 0, "Mod localization validation failed.");
            Assert(typeof(ModIoClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName == ".NETCoreApp,Version=v8.0", "ModIo net8 package asset was not selected.");
            using var mockHttp = new HttpClient(new OfflineApiHandler());
            var credentials = new InMemoryModIoCredentials(); credentials.Set("own-synthetic-offline-key");
            var modApi = new ModIoClient(mockHttp, new() { GameId = 7 }, credentials);
            Assert((await modApi.GetModAsync(3)).Id == 3, "Portable offline ModIo request failed inside Godot.");
            using var receiptBytes = new MemoryStream();
            await ModReceiptDocuments.WriteAsync(receiptBytes, new() { PackageId = "example_mod:mod", Namespace = "example_mod", Version = "1.0.0", ArchiveSha256 = new('a', 64), Origin = new() { Provider = "local" } });
            receiptBytes.Position = 0; Assert((await ModReceiptDocuments.ReadAsync(receiptBytes)).Origin.Provider == "local", "Portable receipt schema failed inside Godot.");
            Assert(typeof(ModKitInstaller).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName == ".NETCoreApp,Version=v8.0", "ModKit bridge net8 package asset was not selected.");
            var localWork = Path.Combine(Path.GetTempPath(), "iryomera-godot-mod-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(localWork);
            try
            {
                var archivePath = Path.Combine(localWork, "example.zip");
                await new Iryomera.ModKit.ModArchive().PackAsync(ProjectSettings.GlobalizePath("res://example-mod"), archivePath);
                var localSource = new LocalArchiveSource(new Dictionary<string, string> { ["example"] = archivePath });
                var installed = await new ModRuntime(new ModKitInstaller()).InstallAsync(localSource, "example", Path.Combine(localWork, "installed"));
                Assert(installed.Receipt.Origin.Provider == "local" && installed.Content.Definitions.Count == 13, "Real offline package installation failed in Godot.");
            }
            finally { Directory.Delete(localWork, true); }
            GD.Print("IRYOMERA_GODOT_SMOKE_OK: portable ModKit installation,  portable ModIo mock HTTP/receipt,  net8 package binaries, annotations, diagnostics, mod asset/layout contracts, en/de localization and plurals.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString()); GetTree().Quit(1);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class OfflineApiHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":3,\"game_id\":7,\"name\":\"Own synthetic offline mod\"}") });
    }
}
