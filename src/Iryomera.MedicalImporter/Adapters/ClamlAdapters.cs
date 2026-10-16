using System.Xml;
using System.Xml.Linq;
using System.Text.Json;

namespace Iryomera.MedicalImporter.Adapters;

public sealed class Icd10GmAdapter : ClamlAdapter
{
    public override string SourceId => "icd-10-gm";
    protected override string ExternalPrefix => "ICD10GM:";
    protected override ImportedTermKind TermKind => ImportedTermKind.Disease;
}

public sealed class OpsAdapter : ClamlAdapter
{
    public override string SourceId => "ops";
    protected override string ExternalPrefix => "OPS:";
    protected override ImportedTermKind TermKind => ImportedTermKind.MedicalProcedure;
}

/// <summary>Reads category codes/preferred labels from BfArM ClaML without resolving external DTDs.</summary>
public abstract class ClamlAdapter : IImportAdapter
{
    public abstract string SourceId { get; }
    protected abstract string ExternalPrefix { get; }
    protected abstract ImportedTermKind TermKind { get; }
    public IReadOnlyList<string> Formats => ["xml"];

    public async Task ReadAsync(Stream input, string format, AdapterContext context, CancellationToken cancellationToken)
    {
        if (format != "xml") throw new InvalidDataException("ICD-10-GM/OPS adapters currently support ClaML XML only, not positional metadata CSV.");
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, CloseInput = false,
            MaxCharactersInDocument = context.Options.MaxInputBytes, MaxCharactersFromEntities = 1024
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken);
        if (document.Root?.Name.LocalName != "ClaML") throw new InvalidDataException("Expected a ClaML root element.");
        var title = document.Root.Elements().FirstOrDefault(x => x.Name.LocalName == "Title");
        if (title is not null)
        {
            context.Batch.ObservedSourceVersion = (string?)title.Attribute("version");
            context.Batch.SourceMetadata["title"] = ImportIdentity.Json(title.ToString(SaveOptions.DisableFormatting));
            var name = (string?)title.Attribute("name");
            if (name is not null && ((SourceId == "ops" && !name.Contains("OPS", StringComparison.OrdinalIgnoreCase)) ||
                (SourceId == "icd-10-gm" && !name.Contains("ICD", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("ClaML title does not match the selected source adapter.");
        }
        foreach (var element in document.Root.Elements().Where(x => x.Name.LocalName == "Class"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = (string?)element.Attribute("code") ?? "";
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException("ClaML class code is required.");
            if ((string?)element.Attribute("kind") != "category")
            { context.Diagnostic(DiagnosticSeverity.Information, "classification-group", code, "Non-category class is not represented as a disease or procedure."); continue; }
            var rubric = element.Elements().FirstOrDefault(x => x.Name.LocalName == "Rubric" && (string?)x.Attribute("kind") == "preferred");
            var label = rubric?.Elements().FirstOrDefault(x => x.Name.LocalName == "Label");
            var language = (string?)label?.Attribute(XNamespace.Xml + "lang") ?? "de";
            var fields = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal) { ["classXml"] = ImportIdentity.Json(element.ToString(SaveOptions.DisableFormatting)), ["code"] = ImportIdentity.Json(code) };
            context.Term(ExternalPrefix + code, label?.Value ?? "", TermKind, fields, language);
            foreach (var parent in element.Elements().Where(x => x.Name.LocalName == "SuperClass"))
                context.Assertion(SourceAssertionKind.OntologyHierarchy, ExternalPrefix + code, "is_a", ExternalPrefix + (string?)parent.Attribute("code"), fields);
        }
    }
}
