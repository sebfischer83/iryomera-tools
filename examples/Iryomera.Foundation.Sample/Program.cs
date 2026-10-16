using System.Globalization;
using Iryomera.Content.SDK;
using Iryomera.Localization;

var data = Path.Combine(AppContext.BaseDirectory, "data");
await using var input = File.OpenRead(Path.Combine(data, "content.json"));
var result = await ContentDocuments.ReadValidatedAsync<ContentPackage>(input);
if (!result.IsValid)
{
    foreach (var issue in result.Issues) Console.Error.WriteLine($"{issue.Code} {issue.Path}: {issue.Message}");
    return 1;
}
var package = result.Value!;
var issues = result.Issues;
var catalogs = new List<LocalizationDefinition>();
foreach (var language in new[] { "en", "de" })
{
    await using var stream = File.OpenRead(Path.Combine(data, language + ".json"));
    catalogs.Add(await ContentJson.ReadAsync<LocalizationDefinition>(stream));
}
var usedKeys = LocalizationValidator.GetContentKeys(package).Concat(new[]
{
    "synthetic.ui.patients", "synthetic.ui.cost", "synthetic.ui.welcome", "synthetic.ui.positional", "synthetic.mod.name"
});
issues = issues.Concat(LocalizationValidator.Validate(catalogs, usedKeys)).ToArray();
if (issues.Count != 0)
{
    foreach (var issue in issues) Console.Error.WriteLine($"{issue.Code} {issue.Path}: {issue.Message}");
    return 1;
}
Console.WriteLine($"Valid: {package.Definitions.Count} synthetic definitions, {package.Relations.Count} relations (not medical knowledge).");
var localizer = new ContentLocalizer(catalogs);
foreach (var language in new[] { "en", "de" })
{
    var culture = CultureInfo.GetCultureInfo(language);
    foreach (var definition in package.Definitions.Where(x => x is DiseaseDefinition or ExaminationDefinition or TreatmentDefinition).Take(5))
        Console.WriteLine($"{definition.Id} [{language}]: {localizer.Format(definition.NameKey, culture)}");
    Console.WriteLine(localizer.Format("synthetic.ui.patients", culture, new Dictionary<string, object?> { ["count"] = 2 }));
    Console.WriteLine(localizer.Format("synthetic.ui.cost", culture, new Dictionary<string, object?> { ["amount"] = 1234.5m }));
}
return 0;
