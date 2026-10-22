using System.Text.Json;
using Iryomera.Content.SDK;
using Iryomera.Localization;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>The generated demo clinic (ADR 0018) and the furnitureEquipment relation; every value is a fictional game fixture.</summary>
public sealed class DemoClinicTests
{
    private const string Ns = "demo_clinic:";
    private static Task<ContentPackage> Package() => Fixtures.Load<ContentPackage>("demo-clinic/content");
    private static Task<LocalizationDefinition> Catalog(string language) => Fixtures.Load<LocalizationDefinition>("demo-clinic/" + language);

    [Fact]
    public async Task GeneratedDemoClinicIsAValidBilingualSyntheticPackage()
    {
        await using (var raw = File.OpenRead(Fixtures.Example("demo-clinic/content")))
        {
            var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(raw);
            Assert.True(read.IsValid, string.Join(';', read.Issues));
        }
        var package = await Package();
        Assert.Empty(ContentValidator.Validate(package));
        var catalogs = new List<LocalizationDefinition>();
        foreach (var language in new[] { "en", "de" })
        {
            await using var input = File.OpenRead(Fixtures.Example("demo-clinic/" + language));
            var catalog = await LocalizationDocuments.ReadValidatedAsync(input, catalogs, requiredLanguages: [language]);
            Assert.True(catalog.IsValid, string.Join(';', catalog.Issues));
            catalogs.Add(catalog.Value!);
        }
        Assert.Empty(ContentLocalizationValidator.Validate(package, catalogs));
        Assert.Equal((7, 5, 3, 1, 2, 7, 8, 5, 20, 3), (Count<RoomDefinition>(), Count<MedicalDepartmentDefinition>(), Count<StaffRoleDefinition>(), Count<StaffQualificationDefinition>(),
            Count<MedicalEquipmentDefinition>(), Count<ObservationDefinition>(), Count<ExaminationDefinition>(), Count<DiseaseDefinition>(), Count<FurnitureDefinition>(), Count<InsurerDefinition>()));
        Assert.All(package.Definitions.Select(d => d.Provenance).Concat(package.Relations.Select(r => r.Provenance)), p =>
            Assert.True(p is { License: "MIT", ReviewStatus: ReviewStatus.Unreviewed } && p.Source.Contains("fictional", StringComparison.Ordinal)));
        Assert.Equal("demo:blood-pressure", package.Definitions.Single(d => d.Id == Ns + "blood_pressure").Provenance.ExternalId);
        Assert.All(package.DiagnosticSimulation!.ObservationDistributions, d => Assert.True(d.SimulationOnly));
        Assert.Empty(package.ReferenceRanges); Assert.Empty(package.DiagnosticRecommendations);

        int Count<T>() where T : ContentDefinition => package.Definitions.OfType<T>().Count();
    }

    [Fact]
    public async Task FixtureValuesMapToTheSdkModel()
    {
        var package = await Package(); ContentPackage[] set = [package];
        var lab = package.Definitions.OfType<RoomDefinition>().Single(r => r.Id == Ns + "room_type_laboratory");
        Assert.Equal((6, true), (lab.MinimumCells!.Value, lab.RequiresDoor!.Value));
        var gameplay = GameplayResolver.Resolve(set);
        Assert.All(gameplay.Balances, b => Assert.Null(b.Capacity)); // abstract department capacity is no longer used (M9)
        Assert.Equal(1500m, gameplay.Balances.Single(b => b.DefinitionId == Ns + "imaging").ExaminationDurationSeconds);
        decimal? Field(string id, Func<GameplayBalance, decimal?> field) => field(gameplay.Balances.Single(b => b.DefinitionId == Ns + id));
        Assert.Equal((600m, 1200m, 60000m, 80m, 60m, 35m), (Field("imaging", b => b.ExaminationCost)!.Value, Field("link_diseasetreatment_green_treatment_operation", b => b.TreatmentCost)!.Value,
            Field("equipment_imaging_ring", b => b.PurchaseCost)!.Value, Field("furniture_disinfectant_dispenser", b => b.PurchaseCost)!.Value,
            Field("role_physician", b => b.WagePerHour)!.Value, Field("role_technician", b => b.WagePerHour)!.Value));
        Assert.Equal(20, gameplay.Balances.Count(b => b.PurchaseCost is not null));
        var surgery = Ns + "department_surgery";
        Assert.Equal([(Ns + "room_type_consultation", RelationRequirement.Required, false), (Ns + "room_type_operating", RelationRequirement.Required, true), (Ns + "room_type_ward", RelationRequirement.Required, false)],
            Departments.Rooms(set, surgery).Select(r => (r.RoomId, r.Requirement, r.TreatmentRoom)));
        Assert.Equal([Ns + "department_internal"], Departments.Prerequisites(set, surgery));
        Assert.DoesNotContain(Departments.Rooms(set, Ns + "department_diagnostics"), r => r.TreatmentRoom);
        Assert.Equal((50000m, 60m, 8m, 20m), (Field("department_surgery", b => b.UnlockCost)!.Value, Field("department_surgery", b => b.UnlockDischargedPatients)!.Value,
            Field("department_surgery", b => b.OpenFromHour)!.Value, Field("department_surgery", b => b.OpenToHour)!.Value));
        Assert.Equal((0m, 0m, 8m, 18m), (Field("department_general", b => b.UnlockCost)!.Value, Field("department_general", b => b.UnlockDischargedPatients)!.Value,
            Field("department_general", b => b.OpenFromHour)!.Value, Field("department_general", b => b.OpenToHour)!.Value));
        Assert.Equal((0m, 24m), (Field("department_emergency", b => b.OpenFromHour)!.Value, Field("department_emergency", b => b.OpenToHour)!.Value));
        PatientArrival Arrival(string id) => gameplay.Balances.Single(b => b.DefinitionId == Ns + id).Arrival!.Value;
        Assert.Equal((PatientArrival.WalkIn, PatientArrival.Emergency, PatientArrival.Emergency), (Arrival("blue"), Arrival("green"), Arrival("silver")));
        var silver = Treatments.Options(set, Ns + "silver");
        Assert.Equal([(Ns + "treatment_operation", 1), (Ns + "treatment_conservative", 2)], silver.Select(o => (o.TreatmentId, o.Priority!.Value)));
        var amber = Treatments.Options(set, Ns + "amber").Single();
        var effective = Treatments.Effective(gameplay, Ns + "amber", amber.TreatmentId, amber.RelationId);
        Assert.Equal((1800m, 900m, 0.8m, 28800m, 57600m), (effective.TreatmentDurationSeconds!.Value, effective.TreatmentCost!.Value, effective.StayChance!.Value, effective.StayDurationSeconds!.Value, effective.StayDurationMaxSeconds!.Value));
        Assert.All(effective.Sources.Values, s => Assert.Equal(TreatmentValueSource.Pair, s));
        var violet = Treatments.Effective(gameplay, Ns + "violet", Ns + "treatment_internal", Treatments.Options(set, Ns + "violet").Single().RelationId);
        Assert.Equal((1000m, 0.6m), (violet.TreatmentCost!.Value, violet.StayChance!.Value)); // same treatment, different pair values
        var followUp = Treatments.Effective(gameplay, Ns + "green", Ns + "treatment_reoperation", null);
        Assert.Equal((TreatmentValueSource.Treatment, 2700m), (followUp.Sources["treatmentDurationSeconds"], followUp.TreatmentDurationSeconds!.Value));
        Assert.All(package.Definitions.OfType<TreatmentDefinition>(), t => Assert.True(Treatments.Playable(set, t.Id), t.Id));
        Assert.Equal([Ns + "complication_reoperation", Ns + "complication_wound_problem"], Treatments.Complications(set, Ns + "treatment_operation").Select(c => c.ComplicationId));
        var wound = Treatments.Complications(set, Ns + "treatment_operation").Single(c => c.ComplicationId.EndsWith("wound_problem", StringComparison.Ordinal));
        Assert.Equal((0.10m, 21600m), (Field(wound.RelationId.Split(':')[1], b => b.ComplicationChance)!.Value, Field("complication_wound_problem", b => b.ExtraStayDurationSeconds)!.Value));
        var relapse = package.Definitions.OfType<ComplicationDefinition>().Single(c => c.Id == Ns + "complication_relapse");
        Assert.Equal((ComplicationEffect.RequiredExamination, ComplicationTiming.DuringStay), (relapse.Effect, relapse.Timing));
        Assert.Null(gameplay.Balances.Single(b => b.DefinitionId == Ns + "blue").TreatmentCost); // treatment values moved to the pairs
        Assert.Equal([(Ns + "department_general", 1), (Ns + "department_internal", 2)],
            package.Relations.Where(r => r.FromId == Ns + "blue" && r.Kind == RelationKind.DiseaseDepartment).Select(r => (r.ToId, r.Priority!.Value)).OrderBy(x => x.Item2));
        RoomDefinition Room(string id) => package.Definitions.OfType<RoomDefinition>().Single(r => r.Id == Ns + id);
        Assert.Equal((RoomRole.Reception, RoomRole.Waiting, RoomRole.Ward, true), (Room("room_type_reception").Role!.Value, Room("room_type_waiting").Role!.Value, Room("room_type_ward").Role!.Value, Room("room_type_operating").Shareable!.Value));
        Assert.Null(Room("room_type_consultation").Role);
        var waiting = package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + "furniture_waiting_bench").Placement!;
        Assert.Equal((2, 3), (waiting.Footprint.Width, waiting.InteractionPoints.Count(p => p.Kind == "seat")));
        Assert.Equal(RelationRequirement.Required, RoomContents.Placeable(set, Ns + "room_type_ward").Single(i => i.DefinitionId == Ns + "furniture_hospital_bed").Requirement);
        Assert.Equal(3600m, Field("link_diseasetreatment_green_treatment_operation", b => b.TreatmentDurationSeconds));
        var table = package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + "equipment_surgical_table");
        Assert.Equal((1, 3, table.Id), (table.Placement!.Footprint.Width, table.Placement.Footprint.Depth, table.Provenance.ExternalId));
        Assert.Null(table.Placement.InteractionPoints.Single(p => p.Key == "lying").Extensions);
        Assert.Equal(RelationRequirement.Required, RoomContents.Placeable(set, Ns + "room_type_operating").Single(i => i.DefinitionId == table.Id).Requirement);

        string[] Targets(string from, RelationKind kind) => package.Relations.Where(r => r.FromId == Ns + from && r.Kind == kind).Select(r => r.ToId).ToArray();
        Assert.Equal([Ns + "role_technician"], Targets("imaging", RelationKind.ExaminationStaffRole));
        Assert.Equal([Ns + "equipment_scanner"], Targets("imaging", RelationKind.ExaminationEquipment));
        Assert.Equal([Ns + "qualification_imaging"], Targets("imaging", RelationKind.ExaminationQualification));
        Assert.Equal([Ns + "observation_pressure"], Targets("blood_pressure", RelationKind.ExaminationObservation));
        Assert.Equal([(Ns + "department_surgery", 1), (Ns + "department_internal", 2)],
            package.Relations.Where(r => r.FromId == Ns + "silver" && r.Kind == RelationKind.DiseaseDepartment).Select(r => (r.ToId, r.Priority!.Value)).ToArray());

        var placeable = RoomContents.Placeable(set, lab.Id);
        Assert.Equal(RelationRequirement.Required, placeable.Single(i => i.DefinitionId == Ns + "equipment_lab_analyser").Requirement);
        Assert.Equal(RelationRequirement.Optional, placeable.Single(i => i.DefinitionId == Ns + "furniture_office_chair").Requirement);
        Assert.Equal([Ns + "equipment_analyzer"], RoomContents.ProvidedEquipment(set, placeable.Select(i => i.DefinitionId)));

        var distributions = package.DiagnosticSimulation!.ObservationDistributions;
        var temperature = distributions.Single(d => d.DiseaseId == Ns + "amber" && d.ObservationId == Ns + "observation_temperature");
        Assert.Equal((37.8m, 39.0m), (temperature.NumericMinimum!.Value, temperature.NumericMaximum!.Value));
        Assert.Equal("Cel", package.Definitions.OfType<ObservationDefinition>().Single(o => o.Id == temperature.ObservationId).Result!.UnitCode);
        Assert.Equal(0.7m, distributions.Single(d => d.DiseaseId == Ns + "violet" && d.ObservationId == Ns + "observation_image").CategoryWeights["marked"]);
        Assert.DoesNotContain(distributions, d => d.ObservationId == Ns + "observation_complaint"); // replaced by symptoms (0.20.2)

        var wheelchair = package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + "furniture_wheelchair");
        Assert.Equal((FurnitureMobility.Pushable, TransportPosture.Seated, 1), (wheelchair.Mobility!.Value, wheelchair.Transport!.Posture, wheelchair.Placement!.Footprint.Width));
        Assert.Equal("iryomera.assets:transport.wheelchair_basic", wheelchair.CatalogAssets!.Single().CatalogId);
        Assert.All(wheelchair.Placement.InteractionPoints, p => Assert.True(p.Extensions!["placeholder"].GetBoolean()));
        InteractionPoint Point(string id, string key) => package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + id).Placement!.InteractionPoints.Single(p => p.Key == key);
        var ct = package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + "equipment_imaging_ring");
        Assert.Equal((2, 3, "iryomera.assets:equipment.ct_scanner_basic"), (ct.Placement!.Footprint.Width, ct.Placement.Footprint.Depth, ct.CatalogAssets!.Single().CatalogId));
        Assert.Equal((1.0m, 0.71m, 1.96m, 180m), (Point("equipment_imaging_ring", "lying").X, Point("equipment_imaging_ring", "lying").Y, Point("equipment_imaging_ring", "lying").Z, Point("equipment_imaging_ring", "lying").HeadingDegrees!.Value));
        Assert.Null(Point("equipment_imaging_ring", "lying").Extensions);
        Assert.True(Point("equipment_imaging_ring", "operator").Extensions!["placeholder"].GetBoolean());
        Assert.Equal(0.82m, Point("furniture_stretcher", "lying").Y);
        Assert.Null(Point("furniture_stretcher", "lying").Extensions);
        Assert.True(Point("furniture_stretcher", "handle").Extensions!["placeholder"].GetBoolean());
        FurniturePlacement Placement(string id) => package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + id).Placement!;
        var bench = Placement("furniture_lab_bench"); var microscope = Placement("equipment_microscope");
        Assert.Null(package.Definitions.OfType<FurnitureDefinition>().Single(f => f.Id == Ns + "furniture_lab_bench").Mobility);
        Assert.Null(bench.Mount);
        Assert.Equal((2m, 1m, "lab_device"), (bench.Surfaces!.Single().Width, bench.Surfaces![0].Depth, bench.Surfaces![0].Accepts!.Single()));
        Assert.True(bench.Surfaces![0].Extensions!["placeholder"].GetBoolean());
        Assert.True(FurnitureSurfaces.Accepts(bench.Surfaces![0], microscope));
        Assert.False(FurnitureSurfaces.Accepts(bench.Surfaces![0], Placement("furniture_ceiling_lamp")));
        Assert.Equal((FurnitureMount.Ceiling, FurnitureMount.Wall, FurnitureMount.Wall), (Placement("furniture_ceiling_lamp").Mount!.Value, Placement("furniture_wall_picture").Mount!.Value, Placement("furniture_disinfectant_dispenser").Mount!.Value));
        Assert.DoesNotContain(package.Relations, r => r.FromId == Ns + "equipment_microscope" && r.Kind == RelationKind.FurnitureEquipment);
        Assert.Equal(8, RoomContents.Placeable(set, lab.Id).Count);

        Assert.Equal(["Alex", "Mika", "Robin", "Jona", "Kim", "Toni", "Sam", "Luca", "Nico", "Chris"], package.NamePools!.Single(p => p.Part == NamePart.Given).Entries.Select(e => e.Localized));
        Assert.Equal("krueger", package.NamePools!.Single(p => p.Part == NamePart.Family).Entries.Single(e => e.Localized == "Krüger").Key);
    }

    [Fact]
    public async Task ComfortAndInsurersMatchTheGameRequest()
    {
        var package = await Package(); ContentPackage[] set = [package];
        var gameplay = GameplayResolver.Resolve(set);
        decimal? Comfort(string id) => gameplay.Balances.SingleOrDefault(b => b.DefinitionId == Ns + id)?.Comfort;
        Assert.Equal((3m, 1m, 2m, 1m, 4m, 3m), (Comfort("furniture_wall_picture")!.Value, Comfort("furniture_ceiling_lamp")!.Value, Comfort("furniture_waiting_bench")!.Value,
            Comfort("furniture_office_chair")!.Value, Comfort("furniture_potted_plant")!.Value, Comfort("furniture_water_dispenser")!.Value));
        Assert.Null(Comfort("furniture_exam_couch"));
        Assert.Equal((300m, 500m), (gameplay.Balances.Single(b => b.DefinitionId == Ns + "furniture_potted_plant").PurchaseCost!.Value,
            gameplay.Balances.Single(b => b.DefinitionId == Ns + "furniture_water_dispenser").PurchaseCost!.Value));
        foreach (var room in new[] { "room_type_waiting", "room_type_reception" })
        {
            var placeable = RoomContents.Placeable(set, Ns + room);
            Assert.All(new[] { "furniture_potted_plant", "furniture_water_dispenser" }, item =>
                Assert.Equal(RelationRequirement.Optional, placeable.Single(i => i.DefinitionId == Ns + item).Requirement));
        }
        Assert.Empty(RoomContents.ProvidedEquipment(set, [Ns + "furniture_potted_plant", Ns + "furniture_water_dispenser"]));

        var insurers = Insurers.All(set, gameplay);
        Assert.Equal([(Ns + "insurer_basic_fund", 0m, 1.0m, 0.6m), (Ns + "insurer_private_cover", 70m, 1.4m, 0.1m), (Ns + "insurer_substitute_fund", 55m, 1.1m, 0.25m)],
            insurers.Select(i => (i.InsurerId, i.MinimumReputation, i.PriceFactor, i.PatientShare)));
        Assert.Equal(0.05m, Insurers.SelfPayShare(insurers));
        Assert.All(package.Definitions.OfType<InsurerDefinition>(), i => Assert.NotNull(i.DescriptionKey));
    }

    [Fact]
    public async Task ObservationRangesOverlapButEveryPairIsSeparable()
    {
        // Synthetic readings (game milestone "diagnostic uncertainty"): ranges overlap, so one finding often fits several
        // cases, but for every pair of cases some observation tells them apart; the middle stays the old fixture value.
        var package = await Package();
        var distributions = package.DiagnosticSimulation!.ObservationDistributions.Where(d => d.TextKey is null).ToArray();
        var diseases = distributions.Select(d => d.DiseaseId).Distinct().Order(StringComparer.Ordinal).ToArray();
        SimulatedObservationDistribution Of(string disease, string observation) => distributions.Single(d => d.DiseaseId == disease && d.ObservationId == observation);
        bool Disjoint(SimulatedObservationDistribution a, SimulatedObservationDistribution b) => a.NumericMinimum is { } min
            ? min > b.NumericMaximum!.Value || b.NumericMinimum!.Value > a.NumericMaximum!.Value
            : !a.CategoryWeights.Any(w => w.Value > 0 && b.CategoryWeights.GetValueOrDefault(w.Key) > 0);
        var observations = distributions.Select(d => d.ObservationId).Distinct().ToArray();
        foreach (var a in diseases)
            foreach (var b in diseases.Where(b => string.CompareOrdinal(a, b) < 0))
                Assert.True(observations.Any(o => Disjoint(Of(a, o), Of(b, o))), $"{a} and {b} cannot be told apart");
        foreach (var a in diseases)
            Assert.True(diseases.Where(b => b != a).Any(b => observations.Any(o => !Disjoint(Of(a, o), Of(b, o)))), $"{a} overlaps nowhere");
        Assert.All(distributions.Where(d => d.NumericMinimum is not null), d => Assert.True(d.NumericMinimum < d.NumericMaximum, d.Id));
        Assert.Contains(distributions, d => d.CategoryWeights.Count > 1);
        var temperature = Of(Ns + "amber", Ns + "observation_temperature");
        Assert.Equal(38.4m, (temperature.NumericMinimum!.Value + temperature.NumericMaximum!.Value) / 2);
    }

    [Fact]
    public async Task SymptomsAndFindingsAreSharedManifestations()
    {
        // Synthetic signs (game milestone "diagnostic uncertainty" D2b): shared symptoms visible on arrival, findings the
        // interview reveals; the old one-complaint-per-case text is gone.
        var package = await Package(); var simulation = package.DiagnosticSimulation!;
        var symptoms = package.Definitions.OfType<SymptomDefinition>().Select(d => d.Id).ToHashSet();
        var findings = package.Definitions.OfType<FindingDefinition>().Select(d => d.Id).ToHashSet();
        Assert.Equal((8, 3), (symptoms.Count, findings.Count));
        Assert.All(symptoms.Concat(findings), sign => Assert.True(simulation.Manifestations.Count(m => m.ManifestationId == sign) >= 2, sign));
        Assert.All(simulation.Manifestations, m => Assert.Equal(symptoms.Contains(m.ManifestationId) ? FindingVisibility.InitiallyVisible : FindingVisibility.AfterExamination, m.Visibility));
        Assert.Equal(0.8m, simulation.Manifestations.Single(m => m.DiseaseId == Ns + "amber" && m.ManifestationId == Ns + "symptom_fatigue").Probability);
        Assert.All(simulation.Manifestations, m => Assert.Contains(package.Relations, r => r.FromId == m.DiseaseId && r.ToId == m.ManifestationId &&
            r.Kind == (symptoms.Contains(m.ManifestationId) ? RelationKind.DiseaseSymptom : RelationKind.DiseaseFinding)));
        Assert.Equal(findings.Order(StringComparer.Ordinal), package.Relations.Where(r => r.FromId == Ns + "interview" && r.Kind == RelationKind.ExaminationFinding)
            .Select(r => r.ToId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(package.Relations, r => r.FromId == Ns + "interview" && r.Kind == RelationKind.ExaminationObservation);
        Assert.DoesNotContain(simulation.ObservationDistributions, d => d.TextKey is not null);
        Assert.Equal("Müdigkeit", (await Catalog("de")).Entries[package.Definitions.Single(d => d.Id == Ns + "symptom_fatigue").NameKey]);
    }

    [Fact]
    public async Task FurnitureEquipmentRoundTripsAndChecksEndpoints()
    {
        var package = await Package();
        var relation = package.Relations.First(r => r.Kind == RelationKind.FurnitureEquipment);
        Assert.True(Fixtures.Evaluate("contentRelation", JsonSerializer.SerializeToNode(relation, ContentJson.Options)).IsValid);
        Assert.Contains("\"furnitureEquipment\"", JsonSerializer.Serialize(relation, ContentJson.Options));
        package.Relations.Add(relation with { Id = Ns + "bad_from", FromId = Ns + "room_type_laboratory" });
        package.Relations.Add(relation with { Id = Ns + "bad_to", ToId = Ns + "furniture_lab_bench" });
        Assert.Equal(2, ContentValidator.Validate(package).Count(i => i.Code == "relation-type"));
        Assert.Empty(RoomContents.ProvidedEquipment([package], []));
        Assert.DoesNotContain("furnitureEquipment", JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options));
    }
}
