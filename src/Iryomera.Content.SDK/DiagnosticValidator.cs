namespace Iryomera.Content.SDK;

internal static class DiagnosticValidator
{
    internal static IReadOnlyList<ValidationIssue> Validate(ContentPackage package, Func<string, ContentDefinition?> resolve)
    {
        var errors = new List<ValidationIssue>();
        void Error(string code, string path, string message) => errors.Add(new(code, path, message));
        ContentDefinition? Reference(string id, string path, params Type[] types)
        {
            var definition = resolve(id);
            if (definition is not null && !types.Contains(definition.GetType())) Error("diagnostic-reference-type", path, "Definition type does not match the diagnostic role.");
            return definition;
        }
        void Context(IEnumerable<PatientContextCriterion> criteria, string path, IEnumerable<string>? declared = null)
        {
            foreach (var c in criteria)
            {
                var count = (c.Minimum.HasValue || c.Maximum.HasValue ? 1 : 0) + (c.Category is not null ? 1 : 0) + (c.BooleanValue.HasValue ? 1 : 0);
                if (!ContentFormat.IsValidLocalizationKey(c.FactorKey) || count != 1 || c.Minimum > c.Maximum || c.Category is { Length: 0 } || (c.UnitCode is not null && !c.Minimum.HasValue && !c.Maximum.HasValue))
                    Error("patient-context", path, "Use a declared stable factor key and exactly one ordered numeric/category/boolean condition.");
                if (declared is not null && !declared.Contains(c.FactorKey, StringComparer.Ordinal)) Error("patient-context-undeclared", path, "Reference-range context is not declared by the observation.");
            }
        }
        foreach (var observation in package.Definitions.OfType<ObservationDefinition>())
        {
            if (observation.Result is not { } result) continue; // Legacy/neutral imported observation has no asserted result shape.
            if (!Enum.IsDefined(result.ValueKind)) Error("observation-kind", observation.Id, "Unknown observation result kind.");
            if (result.UnitCode is not null && (result.ValueKind != ObservationValueKind.Numeric || string.IsNullOrWhiteSpace(result.UnitCode))) Error("observation-unit", observation.Id, "Only numeric observations may specify a nonempty unit.");
            if ((result.ValueKind == ObservationValueKind.Categorical) != (result.Categories.Count > 0)) Error("observation-categories", observation.Id, "Categorical observations require options; other kinds cannot contain options.");
            if (result.Categories.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != result.Categories.Count) Error("duplicate-category", observation.Id, "Category keys must be unique per observation.");
            foreach (var category in result.Categories)
            {
                if (!ContentFormat.IsValidNamespace(category.Key) || !ContentFormat.IsValidLocalizationKey(category.NameKey)) Error("observation-category", observation.Id, "Use a stable category key and localization key.");
                if (category.FindingId is not null) Reference(category.FindingId, observation.Id, typeof(FindingDefinition));
            }
            if (result.PatientContextKeys.Any(k => !ContentFormat.IsValidLocalizationKey(k)) || result.PatientContextKeys.Distinct(StringComparer.Ordinal).Count() != result.PatientContextKeys.Count) Error("patient-context-key", observation.Id, "Patient context keys must be valid, unique stable keys.");
        }
        foreach (var range in package.ReferenceRanges)
        {
            var observation = Reference(range.ObservationId, range.Id, typeof(ObservationDefinition)) as ObservationDefinition;
            if (observation?.Result is not { ValueKind: ObservationValueKind.Numeric } result) Error("reference-range-kind", range.Id, "A reference range requires a numeric result descriptor.");
            else if (result.UnitCode != range.UnitCode) Error("reference-range-unit", range.Id, "Use the observation's exact unit; conversions are not inferred.");
            if ((!range.Minimum.HasValue && !range.Maximum.HasValue) || range.Minimum > range.Maximum || string.IsNullOrWhiteSpace(range.UnitCode)) Error("reference-range-bounds", range.Id, "Reference ranges need nonempty units and ordered bounds.");
            if (range.Provenance.Evidence.Count == 0) Error("reference-range-evidence", range.Id, "Reference intervals require traceable source evidence; fabricated ranges belong to simulation distributions.");
            Context(range.AppliesWhen, range.Id, observation?.Result?.PatientContextKeys);
        }
        foreach (var recommendation in package.DiagnosticRecommendations)
        {
            Reference(recommendation.DiseaseId, recommendation.Id, typeof(DiseaseDefinition));
            Reference(recommendation.ExaminationId, recommendation.Id, typeof(ExaminationDefinition));
            foreach (var context in recommendation.ContextDefinitionIds) Reference(context, recommendation.Id, typeof(SymptomDefinition), typeof(FindingDefinition), typeof(ObservationDefinition));
            if (recommendation.RationaleKey is not null && !ContentFormat.IsValidLocalizationKey(recommendation.RationaleKey)) Error("localization-key", recommendation.Id, "Rationale must reference a stable localization key.");
            if (recommendation.Provenance.Evidence.Count == 0) Error("recommendation-evidence", recommendation.Id, "Diagnostic recommendations require source evidence and explicit medical review.");
            Context(recommendation.AppliesWhen, recommendation.Id);
        }
        var adjacency = package.Relations.Where(r => r.Kind == RelationKind.ExaminationPrerequisite).GroupBy(r => r.FromId).ToDictionary(g => g.Key, g => g.Select(r => r.ToId).ToArray(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal); var active = new HashSet<string>(StringComparer.Ordinal);
        bool Cycle(string id)
        {
            if (active.Contains(id)) return true;
            if (!visited.Add(id)) return false;
            active.Add(id);
            foreach (var parent in adjacency.GetValueOrDefault(id, [])) if (Cycle(parent)) return true;
            active.Remove(id); return false;
        }
        foreach (var id in adjacency.Keys) if (Cycle(id)) { Error("examination-cycle", id, "Examination prerequisites must be acyclic."); break; }
        if (package.DiagnosticSimulation is not { } simulation) return errors;
        foreach (var profile in simulation.ObservationDistributions)
        {
            Reference(profile.DiseaseId, profile.Id, typeof(DiseaseDefinition));
            var descriptor = (Reference(profile.ObservationId, profile.Id, typeof(ObservationDefinition)) as ObservationDefinition)?.Result;
            if (descriptor is null) { Error("simulation-result-kind", profile.Id, "A simulation distribution needs an explicit observation descriptor."); continue; }
            var numeric = profile.NumericMinimum.HasValue || profile.NumericMaximum.HasValue;
            var categories = profile.CategoryWeights.Count > 0;
            var boolean = profile.TrueProbability.HasValue;
            var text = profile.TextKey is not null;
            if ((numeric ? 1 : 0) + (categories ? 1 : 0) + (boolean ? 1 : 0) + (text ? 1 : 0) != 1 ||
                numeric != (descriptor.ValueKind == ObservationValueKind.Numeric) || categories != (descriptor.ValueKind == ObservationValueKind.Categorical) ||
                boolean != (descriptor.ValueKind == ObservationValueKind.Boolean) || text != (descriptor.ValueKind == ObservationValueKind.Text))
                Error("simulation-result-kind", profile.Id, "A distribution must match exactly one declared observation result kind.");
            if (numeric && (!profile.NumericMinimum.HasValue || !profile.NumericMaximum.HasValue || profile.NumericMinimum > profile.NumericMaximum)) Error("simulation-bounds", profile.Id, "Simulated numeric ranges require both ordered bounds.");
            if (categories && (profile.CategoryWeights.Values.Any(v => v < 0) || !profile.CategoryWeights.Values.Any(v => v > 0) || profile.CategoryWeights.Keys.Any(k => !descriptor.Categories.Any(c => c.Key == k)))) Error("simulation-category", profile.Id, "Use declared category keys and nonnegative weights with at least one positive weight.");
            if (profile.TrueProbability is < 0 or > 1) Error("simulation-probability", profile.Id, "Simulation probabilities must be in [0,1].");
            if (profile.TextKey is not null && !ContentFormat.IsValidLocalizationKey(profile.TextKey)) Error("localization-key", profile.Id, "Simulated text must use a localization key.");
            Context(profile.AppliesWhen, profile.Id, descriptor.PatientContextKeys);
        }
        foreach (var manifestation in simulation.Manifestations)
        {
            Reference(manifestation.DiseaseId, manifestation.Id, typeof(DiseaseDefinition)); Reference(manifestation.ManifestationId, manifestation.Id, typeof(SymptomDefinition), typeof(FindingDefinition));
            if (manifestation.Probability is < 0 or > 1 || !Enum.IsDefined(manifestation.Visibility)) Error("simulation-manifestation", manifestation.Id, "Use a valid visibility and simulation probability in [0,1].");
        }
        var policies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in simulation.ExaminationPolicies)
        {
            Reference(policy.ExaminationId, policy.Id, typeof(ExaminationDefinition));
            if (!policies.Add(policy.ExaminationId)) Error("duplicate-examination-policy", policy.Id, "Only one local repeat/visibility policy is allowed per examination.");
            if (!Enum.IsDefined(policy.Visibility) || !Enum.IsDefined(policy.RepeatPolicy) ||
                (policy.RepeatPolicy == ExaminationRepeatPolicy.AfterInterval ? policy.RepeatIntervalSeconds is null or <= 0 : policy.RepeatIntervalSeconds is not null)) Error("simulation-repeat-policy", policy.Id, "Interval repeats require a positive interval; state-change policies must omit it.");
        }
        return errors;
    }
}
