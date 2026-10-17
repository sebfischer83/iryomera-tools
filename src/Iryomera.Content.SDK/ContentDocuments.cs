using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Iryomera.Content.SDK;

/// <summary>Raw-document validation result; Value is available after schema validation, even if semantic checks fail.</summary>
public sealed record ContentReadResult<T>(T? Value, IReadOnlyList<ValidationIssue> Issues) where T : class
{
    public bool IsValid => Value is not null && Issues.Count == 0;
}

/// <summary>
/// Offline JSON Schema + semantic validation against the original JSON before defaults are applied.
/// Uses the exact bundled exchange schemas; no schema is fetched from the network.
/// </summary>
public static class ContentDocuments
{
    private const string SchemaUri = "https://schemas.iryomera.org/v1/contracts.schema.json";
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var resource = typeof(ContentDocuments).Assembly.GetManifestResourceStream("Iryomera.Content.SDK.Schemas.v1.json")
            ?? throw new InvalidOperationException("Bundled content schema resource is missing.");
        using var reader = new StreamReader(resource);
        return JsonSchema.FromText(reader.ReadToEnd());
    });
    private static readonly ConcurrentDictionary<Type, JsonSchema> EntrySchemas = new();
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(AssetReference), typeof(FootprintCell), typeof(FurnitureFootprint), typeof(InteractionPoint), typeof(FurniturePlacement),
        typeof(ObservationResultDefinition), typeof(ObservationCategory), typeof(PatientContextCriterion), typeof(ObservationReferenceRange),
        typeof(DiagnosticRecommendation), typeof(SimulatedObservationDistribution), typeof(SimulatedManifestation), typeof(ExaminationResultPolicy), typeof(DiagnosticSimulationSettings),
        typeof(NamePool), typeof(NamePoolSource), typeof(NameEntry), typeof(NameAttestation), typeof(NameFormat),
        typeof(ContentPackage), typeof(ContentPackageManifest), typeof(ModManifest), typeof(ContentDependency),
        typeof(LocalizationDefinition), typeof(ContentRelation), typeof(MedicalAnnotation), typeof(AnnotationReference),
        typeof(ObservationFrequency), typeof(Provenance), typeof(EvidenceReference), typeof(ExternalIdentifier), typeof(GameplayBalance),
        typeof(ContentDefinition), typeof(DiseaseDefinition), typeof(SymptomDefinition), typeof(FindingDefinition),
        typeof(ExaminationDefinition), typeof(ObservationDefinition), typeof(TreatmentDefinition), typeof(MedicalProcedureDefinition),
        typeof(MedicalDepartmentDefinition), typeof(MedicalEquipmentDefinition), typeof(StaffQualificationDefinition),
        typeof(RoomDefinition), typeof(FurnitureDefinition), typeof(StaffRoleDefinition)
    ];

    /// <summary>
    /// Validate raw JSON shape, then deserialize and run graph/manifest semantics or a supplied validator.
    /// Standalone leaf contracts receive shape checks; graph references are checked on a ContentPackage.
    /// I/O exceptions and cancellation propagate; invalid content returns diagnostics.
    /// </summary>
    public static async Task<ContentReadResult<T>> ReadValidatedAsync<T>(Stream input,
        Func<T, IReadOnlyList<ValidationIssue>>? semanticValidator = null,
        IEnumerable<ContentPackage>? installedPackages = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var schema = EntrySchemas.GetOrAdd(typeof(T), CreateSchema);
        try
        {
            using var document = await JsonDocument.ParseAsync(input,
                new JsonDocumentOptions { AllowDuplicateProperties = false }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var options = new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.List };
            options.SchemaRegistry.Register(Bundle.Value);
            var evaluation = schema.Evaluate(document.RootElement, options);
            if (!evaluation.IsValid)
                return new(null, SchemaIssues(evaluation).Distinct().ToArray());
            var value = typeof(ContentDefinition).IsAssignableFrom(typeof(T))
                ? document.RootElement.Deserialize<ContentDefinition>(ContentJson.Options) as T
                : document.RootElement.Deserialize<T>(ContentJson.Options);
            if (value is null) throw new JsonException("A non-null JSON object with the expected definition kind is required.");
            cancellationToken.ThrowIfCancellationRequested();
            var issues = semanticValidator is not null ? semanticValidator(value) : DefaultSemantics(value, installedPackages);
            return new(value, issues);
        }
        catch (JsonException exception)
        {
            return new(null, [new("json", exception.Path ?? "$", exception.Message)]);
        }
    }

    private static JsonSchema CreateSchema(Type type)
    {
        // Explicit allowlist: callers cannot accidentally skip validation for an unknown CLR type.
        if (!SupportedTypes.Contains(type)) throw new NotSupportedException($"No public exchange schema is registered for {type.FullName}.");
        var name = char.ToLowerInvariant(type.Name[0]) + type.Name[1..];
        return JsonSchema.FromText(new JsonObject { ["$ref"] = SchemaUri + "#/$defs/" + name }.ToJsonString());
    }

    private static IReadOnlyList<ValidationIssue> DefaultSemantics<T>(T value, IEnumerable<ContentPackage>? installedPackages) where T : class => value switch
    {
        ContentPackage package => ContentValidator.Validate(package, installedPackages),
        ModManifest manifest => ManifestValidator.Validate(manifest, installedPackages),
        ContentPackageManifest manifest => ContentValidator.Validate(new() { Manifest = manifest }, installedPackages),
        _ => ContractShape.Validate(value)
    };

    private static IEnumerable<ValidationIssue> SchemaIssues(EvaluationResults results)
    {
        if (results.HasErrors)
            foreach (var (keyword, message) in results.Errors!)
                yield return new("schema-" + keyword, "$" + results.InstanceLocation, message);
        if (results.HasDetails)
            foreach (var detail in results.Details)
                foreach (var issue in SchemaIssues(detail)) yield return issue;
    }
}
