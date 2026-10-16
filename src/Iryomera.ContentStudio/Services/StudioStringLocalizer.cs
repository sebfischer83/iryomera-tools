using Iryomera.Localization;
using Microsoft.Extensions.Localization;

namespace Iryomera.ContentStudio.Services;

/// <summary>UI enum-derived identifiers normalize to the SDK's lowercase stable-key convention.</summary>
public sealed class StudioStringLocalizer(ContentLocalizer catalogs) : IStringLocalizer
{
    private readonly ContentStringLocalizer _inner = new(catalogs);
    public LocalizedString this[string name] => _inner[name.ToLowerInvariant()];
    public LocalizedString this[string name, params object[] arguments] => _inner[name.ToLowerInvariant(), arguments];
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _inner.GetAllStrings(includeParentCultures);
}
