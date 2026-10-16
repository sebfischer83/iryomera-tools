using System.Security.Cryptography;
using Iryomera.Content.SDK;
using Iryomera.ModKit;
using Microsoft.Extensions.DependencyInjection;

namespace Iryomera.ModIo.ModKit;

/// <summary>Explicit new-directory installations using the existing complete ModKit archive policy.</summary>
public sealed class ModKitInstaller(PackageLimits? limits = null) : IModInstaller
{
    public async Task<InstalledMod> InstallAsync(VerifiedArchive archive, string destination, IEnumerable<ContentPackage>? dependencies = null, string? gameVersion = null, CancellationToken ct = default)
    {
        await using (var input = File.OpenRead(archive.Path))
        {
            if (input.Length != archive.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(archive.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive changed after delivery.");
        }
        // Inspect before publishing: invalid origin/receipt cannot leave a successfully installed directory.
        var inspected = await new ModArchive().InspectAsync(archive.Path, dependencies, gameVersion, limits, ct);
        var manifest = inspected.Project.Manifest;
        var receipt = new ModInstallReceipt { PackageId = manifest.Id, Namespace = manifest.Namespace, Version = manifest.Version, ArchiveSha256 = archive.Sha256.ToLowerInvariant(), Origin = archive.Origin };
        using (var check = new MemoryStream()) await ModReceiptDocuments.WriteAsync(check, receipt, ct);
        var result = await new ModArchive().UnpackAsync(archive.Path, destination, dependencies, gameVersion, limits, ct);
        return new(System.IO.Path.GetFullPath(destination), result.Content, receipt);
    }
}

/// <summary>Validated publication bridge. Callers provide their own authorized consent/terms flow and credentials.</summary>
public sealed class ModIoPublisher(IModIoClient api, PackageLimits? limits = null)
{
    public async Task<ModIoFile> UploadAsync(long modId, string archive, string version, string? changelog = null, IEnumerable<ContentPackage>? dependencies = null, string? gameVersion = null, CancellationToken ct = default)
    {
        // Capture and validate exactly the private bytes that will be sent, not a separately reopenable mutable source.
        var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iryomera-publish-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var source = new LocalArchiveSource(new Dictionary<string, string> { ["upload"] = archive }, (limits ?? new()).MaxArchiveBytes);
            var captured = await source.FetchAsync("upload", System.IO.Path.Combine(temporary, "mod.zip"), ct);
            var result = await new ModArchive().InspectAsync(captured.Path, dependencies, gameVersion, limits, ct);
            if (!string.Equals(result.Project.Manifest.Version, version, StringComparison.Ordinal)) throw new InvalidDataException("Upload version must match the content manifest version.");
            await using var input = File.OpenRead(captured.Path); return await api.UploadAsync(modId, input, version, changelog, ct);
        }
        finally { Directory.Delete(temporary, true); }
    }
    public Task<ModIoMod> CreateListingAsync(NewMod details, Stream logo, CancellationToken ct = default) => api.PublishAsync(details, logo, ct);
}
public static class ModKitDependencyInjection
{
    public static IServiceCollection AddIryomeraModKitInstallation(this IServiceCollection services)
    {
        services.AddSingleton<IModInstaller, ModKitInstaller>(); services.AddSingleton<ModRuntime>(); return services;
    }
    public static IServiceCollection AddIryomeraModIoPublishing(this IServiceCollection services)
    {
        services.AddSingleton<ModIoPublisher>(); return services;
    }

}
