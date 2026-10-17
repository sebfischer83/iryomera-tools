using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using Godot;
using Iryomera.Content.SDK;
using Iryomera.Localization;

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
            var furniture = modContent.Value!.Definitions.OfType<FurnitureDefinition>().Single(f => f.Assets is not null);
            Assert(furniture.Assets!.Count == 3 && furniture.Placement!.Footprint.Width == 2 && furniture.Placement.InteractionPoints.Count == 1, "Portable asset/placement contracts were lost.");
            var wallItem = modContent.Value.Definitions.OfType<FurnitureDefinition>().Single(f => f.CatalogAssets is not null);
            Assert(wallItem.CatalogAssets![0].CatalogId == "iryomera.assets:wall.notice_board_example" && wallItem.Placement!.Mount == FurnitureMount.Wall && wallItem.Placement.MountHeightMeters == 1.5m, "Catalog asset/mount contracts were lost.");
            await using var namesSource = File.OpenRead(ProjectSettings.GlobalizePath("res://names.json"));
            var names = await ContentDocuments.ReadValidatedAsync<ContentPackage>(namesSource);
            Assert(names.IsValid, string.Join("; ", names.Issues));
            var family = NamePools.Select([names.Value!], "DE", NamePart.Family, "DE-BY");
            Assert(string.Join(",", family.Select(e => e.Entry.Localized)) == "Brennwald,Quellmar,Almbrunner", "Name pool selection order changed.");
            Assert(NamePools.ResolveFormat([names.Value!], "JP")!.Order == NameOrder.FamilyGiven, "Name format contract was lost.");
            var modCatalogs = new List<LocalizationDefinition>();
            foreach (var language in new[] { "en", "de" })
            {
                await using var input = File.OpenRead(ProjectSettings.GlobalizePath("res://mod-" + language + ".json"));
                modCatalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(input));
            }
            Assert(ContentLocalizationValidator.Validate(modContent.Value, modCatalogs, ["example_mod.mod.name"]).Count == 0, "Mod localization validation failed.");
            GD.Print("IRYOMERA_GODOT_SMOKE_OK: net8 package binaries, annotations, diagnostics, mod asset/layout contracts, en/de localization and plurals.");
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
