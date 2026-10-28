using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

/// <summary>Fictional competing hospitals (ADR 0027); the names are synthetic, no real organisation.</summary>
public sealed class CompetitorTests
{
    [Fact]
    public async Task CompetitorsRoundTripValidateAndMatchTheSchema()
    {
        var package = Fixtures.Package();
        var template = package.Definitions[0];
        package.Definitions.Add(new CompetitorDefinition { Id = "synthetic:linden_hill", NameKey = template.NameKey, Version = "1.0.0", Provenance = template.Provenance });

        Assert.Empty(ContentValidator.Validate(package));
        var node = JsonSerializer.SerializeToNode<ContentDefinition>(package.Definitions.OfType<CompetitorDefinition>().Single(), ContentJson.Options);
        Assert.Equal("competitor", (string?)node!["kind"]);
        Assert.True(Fixtures.Evaluate("competitorDefinition", node).IsValid);
        await using var bytes = new MemoryStream(); await ContentJson.WriteAsync(bytes, package); bytes.Position = 0;
        var read = await ContentDocuments.ReadValidatedAsync<ContentPackage>(bytes);
        Assert.True(read.IsValid, string.Join(';', read.Issues));
        Assert.Equal("synthetic:linden_hill", read.Value!.Definitions.OfType<CompetitorDefinition>().Single().Id);
        Assert.DoesNotContain("competitor", JsonSerializer.Serialize(Fixtures.Package(), ContentJson.Options));
    }
}
