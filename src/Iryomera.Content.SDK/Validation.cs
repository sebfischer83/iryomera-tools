using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Iryomera.Content.SDK;

public sealed record ValidationIssue(string Code, string Path, string Message);

/// <summary>Detect JSON nulls which C# required/non-nullable declarations alone cannot prevent.</summary>
public static class ContractShape
{
    public static IReadOnlyList<ValidationIssue> Validate(object document)
    {
        var issues = new List<ValidationIssue>();
        var nullability = new NullabilityInfoContext();
        Visit(document, "$", 0);
        return issues;

        void Visit(object? value, string path, int depth)
        {
            if (value is null) { issues.Add(new("required", path, "A non-null value is required.")); return; }
            if (depth > 64) { issues.Add(new("depth", path, "Document nesting exceeds 64 levels.")); return; }
            if (value is string || value is JsonElement || value.GetType().IsValueType) return;
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry pair in dictionary) Visit(pair.Value, $"{path}[{pair.Key}]", depth + 1);
                return;
            }
            if (value is IEnumerable list)
            {
                var index = 0;
                foreach (var entry in list) Visit(entry, $"{path}[{index++}]", depth + 1);
                return;
            }
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var child = property.GetValue(value);
                var childPath = $"{path}.{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}";
                if (child is null)
                {
                    if (nullability.Create(property).ReadState == NullabilityState.NotNull)
                        issues.Add(new("required", childPath, "A non-null value is required."));
                }
                else Visit(child, childPath, depth + 1);
            }
        }
    }
}

/// <summary>Offline validation of a package and its complete explicit dependency closure.</summary>
public static class ContentValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, IEnumerable<ContentPackage>? installedPackages = null)
    {
        var errors = new List<ValidationIssue>();
        var packages = new[] { package }.Concat(installedPackages ?? []).ToList();
        foreach (var item in packages) errors.AddRange(ContractShape.Validate(item));
        if (errors.Count > 0) return errors;
        var byId = new Dictionary<string, ContentPackage>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, (ContentDefinition Definition, ContentPackage Package)>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        void Error(string code, string path, string message) => errors.Add(new(code, path, message));
        foreach (var item in packages)
        {
            var path = item.Manifest.Id;
            var manifest = item.Manifest;
            CheckSchema(item.SchemaVersion, path);
            CheckSchema(manifest.SchemaVersion, path + ".manifest");
            CheckId(manifest.Id, path);
            CheckVersion(manifest.Version, path + ".version");
            if (!ContentFormat.IsValidNamespace(manifest.Namespace))
                Error("namespace", path, "Invalid package namespace.");
            CheckOwnership(manifest.Id, manifest.Namespace, path);
            if (!namespaces.Add(manifest.Namespace)) Error("duplicate-namespace", path, "Namespace has multiple owners.");
            if (string.IsNullOrWhiteSpace(manifest.License)) Error("license", path, "A license is required.");
            if (!byId.TryAdd(manifest.Id, item)) Error("duplicate-package", path, "Duplicate package ID.");
            var dependencyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in manifest.Dependencies)
            {
                CheckId(dependency.PackageId, path + ".dependencies");
                if (!dependencyIds.Add(dependency.PackageId)) Error("duplicate-dependency", path, "Dependency is repeated.");
                if (!VersionRange.TryParse(dependency.VersionRange, out _)) Error("version-range", path, "Invalid dependency range.");
            }
            foreach (var definition in item.Definitions)
            {
                var dp = path + ".definitions[" + definition.Id + "]";
                Register(definition.Id, manifest.Namespace, dp);
                CheckVersion(definition.Version, dp);
                CheckKey(definition.NameKey, dp + ".nameKey");
                if (definition.DescriptionKey is not null) CheckKey(definition.DescriptionKey, dp + ".descriptionKey");
                foreach (var key in definition.SynonymKeys) CheckKey(key, dp + ".synonymKeys");
                CheckProvenance(definition.Provenance, dp);
                foreach (var classification in definition.Classifications)
                {
                    if (string.IsNullOrWhiteSpace(classification.System) || string.IsNullOrWhiteSpace(classification.Value))
                        Error("classification", dp, "Classification system and value must be nonempty.");
                    CheckProvenance(classification.Provenance, dp + ".classifications");
                }
                definitions.TryAdd(definition.Id, (definition, item));
            }
            foreach (var range in item.ReferenceRanges) { Register(range.Id, manifest.Namespace, range.Id); CheckSchema(range.SchemaVersion, range.Id); CheckVersion(range.Version, range.Id); CheckProvenance(range.Provenance, range.Id); }
            foreach (var recommendation in item.DiagnosticRecommendations) { Register(recommendation.Id, manifest.Namespace, recommendation.Id); CheckSchema(recommendation.SchemaVersion, recommendation.Id); CheckVersion(recommendation.Version, recommendation.Id); CheckProvenance(recommendation.Provenance, recommendation.Id); }
            if (item.DiagnosticSimulation is { } simulation)
            {
                foreach (var record in simulation.ObservationDistributions.Select(d => (d.Id, d.Version, d.SchemaVersion)).Concat(simulation.Manifestations.Select(m => (m.Id, m.Version, m.SchemaVersion))).Concat(simulation.ExaminationPolicies.Select(p => (p.Id, p.Version, p.SchemaVersion))))
                { Register(record.Id, manifest.Namespace, record.Id); CheckSchema(record.SchemaVersion, record.Id); CheckVersion(record.Version, record.Id); }
            }
            foreach (var relation in item.Relations)
            {
                Register(relation.Id, manifest.Namespace, path + ".relations[" + relation.Id + "]");
                CheckVersion(relation.Version, relation.Id);
                CheckProvenance(relation.Provenance, relation.Id);
            }
            foreach (var annotation in item.MedicalAnnotations)
            {
                Register(annotation.Id, manifest.Namespace, path + ".medicalAnnotations[" + annotation.Id + "]");
                CheckSchema(annotation.SchemaVersion, annotation.Id);
                CheckVersion(annotation.Version, annotation.Id);
                CheckProvenance(annotation.Provenance, annotation.Id);
                if (!Enum.IsDefined(annotation.Kind)) Error("annotation-kind", annotation.Id, "Unknown medical annotation kind.");
                if (string.IsNullOrWhiteSpace(annotation.Predicate)) Error("annotation-predicate", annotation.Id, "The original source predicate is required.");
                if (annotation.Qualifiers.Any(string.IsNullOrWhiteSpace)) Error("annotation-qualifier", annotation.Id, "Qualifier values must be nonempty when present.");
                if (annotation.Frequency is { } frequency && string.IsNullOrWhiteSpace(frequency.SourceValue))
                    Error("annotation-frequency", annotation.Id, "Preserve a nonempty original frequency string; do not invent a numeric probability.");
            }
        }
        // Resolve the package graph before validating references: only declared dependencies are visible.
        foreach (var item in packages)
        {
            foreach (var dependency in item.Manifest.Dependencies)
            {
                if (!byId.TryGetValue(dependency.PackageId, out var target))
                    Error("missing-dependency", item.Manifest.Id, $"Missing package {dependency.PackageId}.");
                else if (VersionRange.TryParse(dependency.VersionRange, out var range) &&
                         NuGetVersion.TryParse(target.Manifest.Version, out var version) && !range.Satisfies(version))
                    Error("dependency-version", item.Manifest.Id, $"Package {dependency.PackageId} does not satisfy {dependency.VersionRange}.");
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Walk(ContentPackage current)
            {
                if (!visiting.Add(current.Manifest.Id)) { Error("dependency-cycle", item.Manifest.Id, "Cyclic package dependencies."); return; }
                if (visited.Add(current.Manifest.Id))
                    foreach (var d in current.Manifest.Dependencies)
                        if (byId.TryGetValue(d.PackageId, out var next)) Walk(next);
                visiting.Remove(current.Manifest.Id);
            }
            Walk(item);
            var allowed = visited;
            (ContentDefinition Definition, ContentPackage Package)? Resolve(string id, string path)
            {
                CheckId(id, path);
                if (!definitions.TryGetValue(id, out var found)) { Error("missing-reference", path, $"Unknown definition {id}."); return null; }
                if (!allowed.Contains(found.Package.Manifest.Id)) Error("undeclared-dependency", path, $"Reference to {id} requires a declared dependency.");
                return found;
            }
            foreach (var relation in item.Relations)
            {
                var from = Resolve(relation.FromId, relation.Id + ".fromId");
                var to = Resolve(relation.ToId, relation.Id + ".toId");
                var types = RelationTypes(relation.Kind);
                if (types is null) Error("relation-kind", relation.Id, "Unknown relation kind.");
                else if ((from is not null && from.Value.Definition.GetType() != types.Value.From) ||
                         (to is not null && to.Value.Definition.GetType() != types.Value.To))
                    Error("relation-type", relation.Id, "Endpoint definition types do not match the relation kind.");
            }
            foreach (var annotation in item.MedicalAnnotations)
            {
                var subjectTypes = annotation.Kind == MedicalAnnotationKind.DiseasePhenotype
                    ? new[] { typeof(DiseaseDefinition) }
                    : new[] { typeof(MedicalProcedureDefinition), typeof(TreatmentDefinition), typeof(ExaminationDefinition) };
                CheckAnnotationReference(annotation.Subject, annotation.Id + ".subject", true, subjectTypes);
                CheckAnnotationReference(annotation.Object, annotation.Id + ".object", true, typeof(FindingDefinition), typeof(SymptomDefinition));
                if (annotation.DiseaseContext is not null) CheckAnnotationReference(annotation.DiseaseContext, annotation.Id + ".diseaseContext", true, typeof(DiseaseDefinition));
                if (annotation.ExtensionContext is not null) CheckAnnotationReference(annotation.ExtensionContext, annotation.Id + ".extensionContext", false);
                if (annotation.Onset is not null) CheckAnnotationReference(annotation.Onset, annotation.Id + ".onset", false);
                if (annotation.Frequency?.Term is not null) CheckAnnotationReference(annotation.Frequency.Term, annotation.Id + ".frequency.term", false);
                foreach (var modifier in annotation.Modifiers) CheckAnnotationReference(modifier, annotation.Id + ".modifiers", false);
            }
            void CheckAnnotationReference(AnnotationReference reference, string path, bool requireResolution, params Type[] expectedTypes)
            {
                if (string.IsNullOrWhiteSpace(reference.ExternalId)) Error("annotation-external-id", path, "The unchanged source identifier is required.");
                if (reference.ContentId is null)
                {
                    if (requireResolution) Error("unresolved-annotation-reference", path, $"Source reference {reference.ExternalId} has no explicit content resolution.");
                    return;
                }
                var resolved = Resolve(reference.ContentId, path + ".contentId");
                if (resolved is not null && expectedTypes.Length > 0 && !expectedTypes.Contains(resolved.Value.Definition.GetType()))
                    Error("annotation-type", path, "Resolved definition type does not match its annotation role.");
            }
            errors.AddRange(DiagnosticValidator.Validate(item, id => Resolve(id, id)?.Definition));
            errors.AddRange(AssetValidator.Validate(item, id => Resolve(id, id)?.Definition));
            var balances = new HashSet<string>(StringComparer.Ordinal);
            foreach (var balance in item.Gameplay)
            {
                Resolve(balance.DefinitionId, balance.DefinitionId + ".gameplay");
                if (!balances.Add(balance.DefinitionId)) Error("duplicate-balance", balance.DefinitionId, "Multiple balance records for one definition.");
                if (new[] { balance.Rarity, balance.Severity, balance.DiagnosticDifficulty, balance.SimulatedSuccessProbability }.Any(x => x is < 0 or > 1))
                    Error("balance-range", balance.DefinitionId, "Normalized simulation values must be within [0,1].");
                if (new[] { balance.TreatmentDurationSeconds, balance.ExaminationDurationSeconds, balance.ExaminationCost, balance.TreatmentCost }.Any(x => x < 0) || balance.RequiredResources.Values.Any(x => x < 0))
                    Error("balance-range", balance.DefinitionId, "Simulation time, costs and resources must be nonnegative.");
                foreach (var resource in balance.RequiredResources.Keys)
                    if (string.IsNullOrWhiteSpace(resource)) Error("resource-key", balance.DefinitionId, "Resource keys must be nonempty.");
            }
        }
        return errors;

        void CheckSchema(string version, string path)
        {
            if (version != ContentFormat.SchemaVersion) Error("schema-version", path, $"Unsupported schema version {version}.");
        }
        void CheckId(string id, string path)
        {
            if (!ContentFormat.IsValidId(id)) Error("id", path, $"Invalid namespace-based ID {id}.");
        }
        void CheckOwnership(string id, string ns, string path)
        {
            if (!id.StartsWith(ns + ":", StringComparison.Ordinal)) Error("namespace-owner", path, "ID does not belong to the package namespace.");
        }
        void Register(string id, string ns, string path)
        {
            CheckId(id, path); CheckOwnership(id, ns, path);
            if (!ids.Add(id)) Error("duplicate-id", path, $"Duplicate content ID {id}.");
        }
        void CheckVersion(string version, string path)
        {
            if (!IsVersion(version)) Error("version", path, "Expected a canonical SemVer 2 version.");
        }
        void CheckKey(string key, string path)
        {
            if (!ContentFormat.IsValidLocalizationKey(key)) Error("localization-key", path, "Expected a stable dotted localization key.");
        }
        void CheckProvenance(Provenance p, string path)
        {
            if (new[] { p.Source, p.ExternalId, p.SourceVersion, p.License }.Any(string.IsNullOrWhiteSpace) || p.ImportedAt == default)
                Error("provenance", path, "Source, external ID, source version, license and import timestamp are required.");
            if (!Enum.IsDefined(p.ReviewStatus)) Error("review-status", path, "Unknown review status.");
            if ((p.ReviewStatus is ReviewStatus.Approved or ReviewStatus.Rejected) && (string.IsNullOrWhiteSpace(p.ReviewedBy) || p.ReviewedAt is null))
                Error("review-audit", path, "Approval or rejection requires a reviewer and timestamp.");
            foreach (var e in p.Evidence)
                if (string.IsNullOrWhiteSpace(e.Citation) || (e.Uri is not null && !Uri.TryCreate(e.Uri, UriKind.Absolute, out _)))
                    Error("evidence", path, "Evidence requires a citation and an absolute URI when provided.");
        }
    }

    public static bool IsVersion(string value) => NuGetVersion.TryParse(value, out var version) && version.ToFullString() == value &&
        Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?\z");

    public static (Type From, Type To)? RelationTypes(RelationKind kind) => kind switch
    {
        RelationKind.DiseaseSymptom => (typeof(DiseaseDefinition), typeof(SymptomDefinition)),
        RelationKind.DiseaseFinding => (typeof(DiseaseDefinition), typeof(FindingDefinition)),
        RelationKind.ExaminationFinding => (typeof(ExaminationDefinition), typeof(FindingDefinition)),
        RelationKind.DiseaseExamination => (typeof(DiseaseDefinition), typeof(ExaminationDefinition)),
        RelationKind.DiseaseTreatment => (typeof(DiseaseDefinition), typeof(TreatmentDefinition)),
        RelationKind.ExaminationEquipment => (typeof(ExaminationDefinition), typeof(MedicalEquipmentDefinition)),
        RelationKind.TreatmentQualification => (typeof(TreatmentDefinition), typeof(StaffQualificationDefinition)),
        RelationKind.ExaminationObservation => (typeof(ExaminationDefinition), typeof(ObservationDefinition)),
        RelationKind.ExaminationRoom => (typeof(ExaminationDefinition), typeof(RoomDefinition)),
        RelationKind.ExaminationQualification => (typeof(ExaminationDefinition), typeof(StaffQualificationDefinition)),
        RelationKind.ExaminationPrerequisite => (typeof(ExaminationDefinition), typeof(ExaminationDefinition)),
        RelationKind.RoomFurniture => (typeof(RoomDefinition), typeof(FurnitureDefinition)),
        _ => null
    };
}
