using System.Text.Json;
using System.Text.Json.Nodes;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class DiagnosticContentTests
{
    private static Task<ContentPackage> Package() => Fixtures.Load<ContentPackage>("diagnostics");
    [Fact]
    public async Task FiveSharedExaminationsAndThreeDiseasesFormAValidBilingualDefinitionPrototype()
    {
        var package = await Package();
        Assert.Empty(ContentValidator.Validate(package));
        Assert.Equal(3, package.Definitions.OfType<DiseaseDefinition>().Count()); Assert.Equal(5, package.Definitions.OfType<ExaminationDefinition>().Count());
        Assert.Equal(15, package.Relations.Count(r => r.Kind == RelationKind.DiseaseExamination));
        Assert.Equal(5, package.Relations.Count(r => r.Kind == RelationKind.ExaminationObservation));
        Assert.Equal(4, package.Definitions.OfType<ObservationDefinition>().Select(o => o.Result!.ValueKind).Distinct().Count());
        var catalogs = new[] { await Fixtures.Load<LocalizationDefinition>("diagnostics.en"), await Fixtures.Load<LocalizationDefinition>("diagnostics.de") };
        Assert.Empty(LocalizationValidator.Validate(catalogs, LocalizationValidator.GetContentKeys(package), ["en", "de"]));
        Assert.All(StudioIndependentRecords(package), p => Assert.Equal(ReviewStatus.Unreviewed, p.ReviewStatus));
        Assert.All(package.DiagnosticSimulation!.ObservationDistributions, p => Assert.True(p.SimulationOnly));
    }
    private static IEnumerable<Provenance> StudioIndependentRecords(ContentPackage p) => p.Definitions.Select(d => d.Provenance).Concat(p.ReferenceRanges.Select(r => r.Provenance)).Concat(p.DiagnosticRecommendations.Select(r => r.Provenance));

    [Fact]
    public async Task RawSchemasAndRoundTripCoverEveryNewDiagnosticContract()
    {
        var package = await Package();
        await using var stream = new MemoryStream(); await ContentJson.WriteAsync(stream, package); stream.Position = 0;
        Assert.True((await ContentDocuments.ReadValidatedAsync<ContentPackage>(stream)).IsValid);
        var leaves = new (string Name, object Value)[]
        {
            ("observationResultDefinition", package.Definitions.OfType<ObservationDefinition>().First().Result!),
            ("observationCategory", package.Definitions.OfType<ObservationDefinition>().First(o => o.Result!.Categories.Count > 0).Result!.Categories[0]),
            ("patientContextCriterion", package.ReferenceRanges[0].AppliesWhen[0]),
            ("observationReferenceRange", package.ReferenceRanges[0]), ("diagnosticRecommendation", package.DiagnosticRecommendations[0]),
            ("simulatedObservationDistribution", package.DiagnosticSimulation!.ObservationDistributions[0]),
            ("simulatedManifestation", package.DiagnosticSimulation.Manifestations[0]), ("examinationResultPolicy", package.DiagnosticSimulation.ExaminationPolicies[0]),
            ("diagnosticSimulationSettings", package.DiagnosticSimulation)
        };
        foreach (var (name, value) in leaves) Assert.True(Fixtures.Evaluate(name, JsonSerializer.SerializeToNode(value, value.GetType(), ContentJson.Options)).IsValid, name);
        var invalid = JsonSerializer.SerializeToNode(package, ContentJson.Options)!; invalid["diagnosticSimulation"]!["simulationOnly"] = false;
        Assert.False(Fixtures.Evaluate("contentPackage", invalid).IsValid);
    }
    [Theory]
    [InlineData("unit", "reference-range-unit")]
    [InlineData("bounds", "reference-range-bounds")]
    [InlineData("evidence", "reference-range-evidence")]
    [InlineData("context", "patient-context-undeclared")]
    [InlineData("kind", "reference-range-kind")]
    public async Task ReferenceIntervalsRequireCorrectUnitsContextTypeBoundsAndEvidence(string change, string code)
    {
        var p = await Package(); var r = p.ReferenceRanges[0];
        p.ReferenceRanges[0] = change switch
        {
            "unit" => r with { UnitCode = "different" }, "bounds" => r with { Minimum = 10, Maximum = 1 },
            "evidence" => r with { Provenance = r.Provenance with { Evidence = [] } },
            "context" => r with { AppliesWhen = [new() { FactorKey = "patient.undeclared", Category = "test" }] },
            _ => r with { ObservationId = p.Definitions.OfType<ObservationDefinition>().First(o => o.Result!.ValueKind == ObservationValueKind.Boolean).Id }
        };
        Assert.Contains(ContentValidator.Validate(p), i => i.Code == code);
    }
    [Theory]
    [InlineData("probability", "simulation-probability")]
    [InlineData("bounds", "simulation-bounds")]
    [InlineData("category", "simulation-category")]
    [InlineData("kind", "simulation-result-kind")]
    [InlineData("repeat", "simulation-repeat-policy")]
    public async Task SimulationConfigurationIsTypedAndCannotMasqueradeAsMedicalValues(string change, string code)
    {
        var p = await Package(); var simulation = p.DiagnosticSimulation!;
        if (change == "repeat") simulation.ExaminationPolicies[0] = simulation.ExaminationPolicies[0] with { RepeatPolicy = ExaminationRepeatPolicy.AfterInterval };
        else
        {
            var i = simulation.ObservationDistributions.FindIndex(d => change == "probability" ? d.TrueProbability.HasValue : change == "bounds" ? d.NumericMinimum.HasValue : d.CategoryWeights.Count > 0);
            var d = simulation.ObservationDistributions[i];
            simulation.ObservationDistributions[i] = change switch
            { "probability" => d with { TrueProbability = 2 }, "bounds" => d with { NumericMinimum = 99, NumericMaximum = 1 }, "category" => d with { CategoryWeights = new() { ["unknown"] = 1 } }, _ => d with { TrueProbability = 0.5m } };
        }
        Assert.Contains(ContentValidator.Validate(p), i => i.Code == code);
    }
    [Fact]
    public async Task RecommendationEndpointsEvidenceAndDuplicateIdsAreChecked()
    {
        var p = await Package(); var recommendation = p.DiagnosticRecommendations[0];
        p.DiagnosticRecommendations[0] = recommendation with { DiseaseId = p.Definitions.OfType<ExaminationDefinition>().First().Id, Id = p.Definitions[0].Id, Provenance = recommendation.Provenance with { Evidence = [] } };
        var issues = ContentValidator.Validate(p);
        Assert.Contains(issues, i => i.Code == "diagnostic-reference-type"); Assert.Contains(issues, i => i.Code == "recommendation-evidence"); Assert.Contains(issues, i => i.Code == "duplicate-id");
    }
    [Fact]
    public async Task PrerequisiteCyclesAndWrongRelationEndpointTypesAreRejected()
    {
        var p = await Package(); var original = p.Relations.Single(r => r.Kind == RelationKind.ExaminationPrerequisite);
        p.Relations.Add(original with { Id = "diagnostics_demo:cycle", FromId = original.ToId, ToId = original.FromId });
        p.Relations.Add(original with { Id = "diagnostics_demo:wrong_type", Kind = RelationKind.ExaminationObservation });
        Assert.Contains(ContentValidator.Validate(p), i => i.Code == "examination-cycle"); Assert.Contains(ContentValidator.Validate(p), i => i.Code == "relation-type");
    }
    [Fact]
    public async Task ModsCanReferenceSharedExaminationsThroughExplicitDependencies()
    {
        var shared = await Package(); var ns = "author.diagnostics";
        var mod = new ContentPackage { Manifest = new() { Id = ns + ":content", Namespace = ns, Version = "1.0.0", License = "MIT", Dependencies = [new() { PackageId = shared.Manifest.Id, VersionRange = "[1.0.0,2.0.0)" }] } };
        var d = shared.Definitions.OfType<DiseaseDefinition>().First(); mod.Definitions.Add(d with { Id = ns + ":disease" });
        mod.Relations.Add(new() { Id = ns + ":shared_test", Kind = RelationKind.DiseaseExamination, Version = "1.0.0", FromId = ns + ":disease", ToId = shared.Definitions.OfType<ExaminationDefinition>().First().Id, Provenance = d.Provenance });
        mod.ReferenceRanges.Add(shared.ReferenceRanges[0] with { Id = ns + ":context_range" });
        mod.DiagnosticRecommendations.Add(shared.DiagnosticRecommendations[0] with { Id = ns + ":recommendation", DiseaseId = ns + ":disease" });
        mod = mod with { DiagnosticSimulation = new() { ObservationDistributions = [shared.DiagnosticSimulation!.ObservationDistributions[0] with { Id = ns + ":distribution", DiseaseId = ns + ":disease" }] } };
        Assert.Empty(ContentValidator.Validate(mod, [shared])); mod.Manifest.Dependencies.Clear();
        Assert.Contains(ContentValidator.Validate(mod, [shared]), i => i.Code == "undeclared-dependency");
    }
    [Fact]
    public async Task NewStandaloneVersionsAreWireRequiredAndLegacyObservationContractsStillLoad()
    {
        var p = await Package(); var node = JsonSerializer.SerializeToNode(p.ReferenceRanges[0], ContentJson.Options)!; node.AsObject().Remove("schemaVersion");
        await using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.False((await ContentDocuments.ReadValidatedAsync<ObservationReferenceRange>(input)).IsValid);
        var legacy = Fixtures.Package(); Assert.Empty(ContentValidator.Validate(legacy));
        var observation = new ObservationDefinition { Id = "synthetic:neutral", NameKey = "synthetic.neutral.name", Version = "1.0.0", Provenance = legacy.Definitions[0].Provenance };
        legacy.Definitions.Add(observation); Assert.Empty(ContentValidator.Validate(legacy)); Assert.Null(observation.Result);
    }

    [Fact]
    public async Task AllDiagnosticReferencesAndResourceRolesAreValidated()
    {
        var p = await Package();
        p.ReferenceRanges[0] = p.ReferenceRanges[0] with { ObservationId = "diagnostics_demo:missing_observation" };
        p.DiagnosticRecommendations[0] = p.DiagnosticRecommendations[0] with { ContextDefinitionIds = ["diagnostics_demo:missing_context"] };
        var policy = p.DiagnosticSimulation!.ExaminationPolicies[0];
        p.DiagnosticSimulation.ExaminationPolicies[0] = policy with { ExaminationId = "diagnostics_demo:missing_examination" };
        var relation = p.Relations.First(r => r.Kind == RelationKind.ExaminationQualification);
        p.Relations[p.Relations.IndexOf(relation)] = relation with { ToId = p.Definitions.OfType<DiseaseDefinition>().First().Id };
        var issues = ContentValidator.Validate(p);
        foreach (var missing in new[] { "missing_observation", "missing_context", "missing_examination" })
            Assert.Contains(issues, i => i.Code == "missing-reference" && i.Path.Contains(missing, StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Code == "relation-type");
    }

    [Fact]
    public async Task CategoricalLabelsAndRecommendationRationalesParticipateInTranslationChecks()
    {
        var p = await Package();
        var en = await Fixtures.Load<LocalizationDefinition>("diagnostics.en");
        var de = await Fixtures.Load<LocalizationDefinition>("diagnostics.de");
        var categoryKey = p.Definitions.OfType<ObservationDefinition>().First(o => o.Result!.Categories.Count > 0).Result!.Categories[0].NameKey;
        var rationaleKey = p.DiagnosticRecommendations[0].RationaleKey!;
        de.Entries.Remove(categoryKey); de.Entries.Remove(rationaleKey);
        var issues = LocalizationValidator.Validate([en, de], LocalizationValidator.GetContentKeys(p), ["en", "de"]);
        Assert.Contains(issues, i => i.Code == "missing-translation" && i.Path.Contains(categoryKey, StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Code == "missing-translation" && i.Path.Contains(rationaleKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidCategoriesAndPatientConditionsFailSemanticValidation()
    {
        var p = await Package();
        var original = p.Definitions.OfType<ObservationDefinition>().First(o => o.Result!.Categories.Count > 0);
        p.Definitions[p.Definitions.IndexOf(original)] = original with { Result = original.Result! with
        { Categories = [original.Result!.Categories[0], original.Result.Categories[0]], UnitCode = "mg/L" } };
        p.ReferenceRanges[0] = p.ReferenceRanges[0] with { AppliesWhen = [new() { FactorKey = "patient.age", Minimum = 1, BooleanValue = true }] };
        var issues = ContentValidator.Validate(p);
        Assert.Contains(issues, i => i.Code == "duplicate-category"); Assert.Contains(issues, i => i.Code == "observation-unit");
        Assert.Contains(issues, i => i.Code == "patient-context");
    }
}
