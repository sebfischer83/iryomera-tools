using System.Text.Json;
using Iryomera.Content.SDK;
using Json.Schema;

namespace Iryomera.ModIo;

/// <summary>Offline validation of original receipt JSON before defaults. Host receipts belong outside mod payloads.</summary>
public static class ModReceiptDocuments
{
    private static readonly Lazy<JsonSchema> Bundle = new(() =>
    {
        using var stream = typeof(ModReceiptDocuments).Assembly.GetManifestResourceStream("Iryomera.ModIo.Schemas.v1.json")!;
        using var reader = new StreamReader(stream); return JsonSchema.FromText(reader.ReadToEnd());
    });
    public static async Task<ModInstallReceipt> ReadAsync(Stream input, CancellationToken ct = default)
    {
        using var bytes = new MemoryStream(); await SafeHttp.CopyAsync(input, bytes, 1024 * 1024, ct); bytes.Position = 0;
        using var document = await JsonDocument.ParseAsync(bytes, new() { AllowDuplicateProperties = false }, ct);
        var schema = JsonSchema.FromText("{\"$ref\":\"https://schemas.iryomera.org/modio/v1/contracts.schema.json#/$defs/modInstallReceipt\"}");
        var options = new EvaluationOptions { RequireFormatValidation = true }; options.SchemaRegistry.Register(Bundle.Value);
        if (!schema.Evaluate(document.RootElement, options).IsValid) throw new InvalidDataException("Installation receipt does not match v1 schema.");
        var receipt = document.RootElement.Deserialize<ModInstallReceipt>(ContentJson.Options) ?? throw new InvalidDataException("Expected a receipt.");
        if (!ContentValidator.IsVersion(receipt.Version)) throw new InvalidDataException("Receipt version must be canonical SemVer.");
        if (!receipt.PackageId.StartsWith(receipt.Namespace + ":", StringComparison.Ordinal)) throw new InvalidDataException("Receipt package namespace mismatch.");
        return receipt;
    }
    public static async Task WriteAsync(Stream output, ModInstallReceipt receipt, CancellationToken ct = default)
    {
        using var captured = new MemoryStream(); await ContentJson.WriteAsync(captured, receipt, ct); captured.Position = 0;
        _ = await ReadAsync(captured, ct); captured.Position = 0; await captured.CopyToAsync(output, ct);
    }
}
