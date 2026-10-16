using System.Text.Json;
using Iryomera.Content.SDK;
using Xunit;

namespace Iryomera.Foundation.Tests;

public sealed class ObservationTests
{
    [Fact]
    public async Task ObservationsRoundTripThroughSchemasOnBothSupportedRuntimes()
    {
        var package = Fixtures.Package();
        var observation = new ObservationDefinition
        { Id = "demo:observation", NameKey = "demo.observation.name", Version = "1.0.0", Provenance = package.Definitions[0].Provenance };
        await using var input = new MemoryStream();
        await ContentJson.WriteAsync(input, observation); input.Position = 0;
        var result = await ContentDocuments.ReadValidatedAsync<ObservationDefinition>(input);
        Assert.True(result.IsValid, string.Join(';', result.Issues));
        Assert.Equal(observation.Id, result.Value!.Id);
        Assert.Contains("\"kind\": \"observation\"", JsonSerializer.Serialize<ContentDefinition>(observation, ContentJson.Options));
    }

    [Fact]
    public void ObservationCannotSatisfyAPlayableExaminationRelation()
    {
        var package = Fixtures.Package();
        var examination = package.Definitions.OfType<ExaminationDefinition>().First();
        package.Definitions.Remove(examination);
        package.Definitions.Add(new ObservationDefinition { Id = examination.Id, NameKey = examination.NameKey, Version = examination.Version, Provenance = examination.Provenance });
        Assert.Contains(ContentValidator.Validate(package), issue => issue.Code == "relation-type");
    }
}
