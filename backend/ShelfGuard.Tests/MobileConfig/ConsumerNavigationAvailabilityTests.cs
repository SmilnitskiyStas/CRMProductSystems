using System.Text.Json.Nodes;
using ShelfGuard.Application.Features.MobileConfig;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

public sealed class ConsumerNavigationAvailabilityTests
{
    [Theory]
    [InlineData("catalog")]
    [InlineData("promotions")]
    [InlineData("coupons")]
    [InlineData("news")]
    public void Explicit_navigation_enables_its_section_without_mutating_the_saved_snapshot(string type)
    {
        var saved = $$"""{"features":{"{{type}}":false},"navigation":[{"type":"{{type}}"}]}""";
        var document = JsonNode.Parse(saved)!.AsObject();
        ConsumerNavigationAvailability.Apply(document);
        Assert.True(document["features"]![type]!.GetValue<bool>());
        Assert.False(JsonNode.Parse(saved)!["features"]![type]!.GetValue<bool>());
        var reconciled = document.ToJsonString();
        ConsumerNavigationAvailability.Apply(document);
        Assert.Equal(reconciled, document.ToJsonString());
    }

    [Fact]
    public void Navigation_never_bypasses_bonus_program_or_unrelated_disabled_flags()
    {
        var document = JsonNode.Parse("""{"features":{"loyalty":false,"catalog":false,"delivery":false},"navigation":[{"type":"loyalty"},{"type":"home"}]}""")!.AsObject();
        ConsumerNavigationAvailability.Apply(document);
        Assert.False(document["features"]!["loyalty"]!.GetValue<bool>());
        Assert.False(document["features"]!["catalog"]!.GetValue<bool>());
        Assert.False(document["features"]!["delivery"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"navigation\":[null,{},17,{\"type\":false}]}")]
    public void Missing_or_unexpected_navigation_does_not_enable_sections(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        ConsumerNavigationAvailability.Apply(document);
        Assert.Null(document["features"]?["catalog"]);
    }
}
