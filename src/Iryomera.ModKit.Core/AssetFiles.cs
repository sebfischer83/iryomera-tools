using System.Buffers.Binary;
using System.Text.Json;

namespace Iryomera.ModKit;

internal static class AssetFiles
{
    internal static async Task ValidateAsync(string path, ModFileKind kind, PackageLimits limits, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > limits.MaxFileBytes) throw new InvalidDataException("Asset exceeds size limit.");
        var header = new byte[20];
        if (kind != ModFileKind.ModelGlb)
        {
            ModValidator.RequireExtension(path, ".png", ".jpg", ".jpeg", ".webp");
            var read = await stream.ReadAtLeastAsync(header, Math.Min(header.Length, (int)Math.Min(stream.Length, header.Length)), false, ct);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var valid = ext switch
            {
                ".png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
                ".jpg" or ".jpeg" => read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255,
                ".webp" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8), _ => false
            };
            if (!valid) throw new InvalidDataException("Image signature does not match its declared type.");
            return;
        }
        ModValidator.RequireExtension(path, ".glb");
        await stream.ReadExactlyAsync(header, ct);
        uint At(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4));
        if (At(0) != 0x46546c67 || At(4) != 2 || At(8) != stream.Length || At(16) != 0x4e4f534a || At(12) == 0 || At(12) % 4 != 0 || At(12) > Math.Min(limits.MaxJsonBytes, 4L * 1024 * 1024) || At(12) > stream.Length - 20)
            throw new InvalidDataException("Invalid GLB v2 header/JSON chunk.");
        var json = new byte[(int)At(12)]; await stream.ReadExactlyAsync(json, ct);
        using var document = JsonDocument.Parse(json, new() { AllowDuplicateProperties = false });
        var root = document.RootElement;
        if (!root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var version) || version.GetString() != "2.0") throw new InvalidDataException("Expected glTF 2.0 asset metadata.");
        void NoExternalUris(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) NoExternalUris(item);
            if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject())
            {
                if (property.Name.Equals("uri", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GLB must use embedded bufferViews; URI resources are not accepted.");
                NoExternalUris(property.Value);
            }
        }
        NoExternalUris(root);
        if (stream.Position < stream.Length)
        {
            var chunk = new byte[8]; await stream.ReadExactlyAsync(chunk, ct);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk);
            if (BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4)) != 0x004e4942 || length % 4 != 0 || length != stream.Length - stream.Position)
                throw new InvalidDataException("Invalid GLB binary chunk/framing.");
        }
    }
}
