using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-650: the shared (de)serialization + validation helper for the
/// <c>supplier_profiles.DeliveryCoverage</c> JSONB string.
/// </summary>
public sealed class DeliveryCoverageJsonTests
{
    // ── Parse ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullOrBlank_ReturnsNull()
    {
        Assert.Null(DeliveryCoverageJson.Parse(null));
        Assert.Null(DeliveryCoverageJson.Parse(""));
        Assert.Null(DeliveryCoverageJson.Parse("   "));
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        Assert.Null(DeliveryCoverageJson.Parse("{ not json"));
        Assert.Null(DeliveryCoverageJson.Parse("\"a string\""));
    }

    [Fact]
    public void Parse_CanonicalCamelCaseShape_RoundTrips()
    {
        const string json =
            """{"served":[{"regionCode":"UA-32","terms":"2-3 дні"}],"notServed":["UA-43"],"note":"Новою Поштою"}""";

        var dto = DeliveryCoverageJson.Parse(json);

        Assert.NotNull(dto);
        var served = Assert.Single(dto!.Served);
        Assert.Equal("UA-32", served.RegionCode);
        Assert.Equal("2-3 дні", served.Terms);
        Assert.Equal(new[] { "UA-43" }, dto.NotServed);
        Assert.Equal("Новою Поштою", dto.Note);
    }

    [Fact]
    public void Parse_ToleratesMissingKeys()
    {
        var dto = DeliveryCoverageJson.Parse("""{"served":[{"regionCode":"UA-30"}]}""");

        Assert.NotNull(dto);
        Assert.Equal("UA-30", dto!.Served[0].RegionCode);
        Assert.Null(dto.Served[0].Terms);
        Assert.Empty(dto.NotServed);
        Assert.Null(dto.Note);
    }

    [Fact]
    public void Parse_NormalizesWhitespaceAndDropsBlanks()
    {
        const string json =
            """{"served":[{"regionCode":" UA-32 ","terms":"  "},{"regionCode":""}],"notServed":["  ","UA-43"],"note":"  "}""";

        var dto = DeliveryCoverageJson.Parse(json);

        Assert.NotNull(dto);
        var served = Assert.Single(dto!.Served);
        Assert.Equal("UA-32", served.RegionCode);
        Assert.Null(served.Terms);                 // whitespace-only terms → null
        Assert.Equal(new[] { "UA-43" }, dto.NotServed);
        Assert.Null(dto.Note);
    }

    // ── Serialize ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesCamelCase_AndParseRoundTrips()
    {
        var dto = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-32", "від 5000 грн") },
            new[] { "UA-40" },
            "note");

        var json = DeliveryCoverageJson.Serialize(dto);

        Assert.Contains("\"served\"", json);
        Assert.Contains("\"regionCode\"", json);
        Assert.Contains("\"notServed\"", json);

        var back = DeliveryCoverageJson.Parse(json);
        Assert.NotNull(back);
        Assert.Equal("UA-32", back!.Served[0].RegionCode);
        Assert.Equal("від 5000 грн", back.Served[0].Terms);
        Assert.Equal(new[] { "UA-40" }, back.NotServed);
        Assert.Equal("note", back.Note);
    }

    [Fact]
    public void Serialize_DedupesServedByRegionCode_KeepingFirst()
    {
        var dto = new DeliveryCoverageDto(
            new[]
            {
                new DeliveryCoverageEntryDto("UA-32", "first"),
                new DeliveryCoverageEntryDto("UA-32", "second"),
            },
            new[] { "UA-43", "UA-43" },
            null);

        var back = DeliveryCoverageJson.Parse(DeliveryCoverageJson.Serialize(dto));

        Assert.NotNull(back);
        var served = Assert.Single(back!.Served);
        Assert.Equal("first", served.Terms);
        Assert.Equal(new[] { "UA-43" }, back.NotServed);
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllKnownCodes_NoOverlap_ReturnsNoErrors()
    {
        var dto = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-32", null), new DeliveryCoverageEntryDto("UA-18-ZHYTOMYR", "х") },
            new[] { "UA-43" },
            "note");

        Assert.Empty(DeliveryCoverageJson.Validate(dto));
    }

    [Fact]
    public void Validate_UnknownRegionCode_ReturnsError()
    {
        var served = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-999", null) }, Array.Empty<string>(), null);
        var notServed = new DeliveryCoverageDto(
            Array.Empty<DeliveryCoverageEntryDto>(), new[] { "NOPE" }, null);

        Assert.NotEmpty(DeliveryCoverageJson.Validate(served));
        Assert.NotEmpty(DeliveryCoverageJson.Validate(notServed));
    }

    [Fact]
    public void Validate_CodeInBothServedAndNotServed_ReturnsError()
    {
        var dto = new DeliveryCoverageDto(
            new[] { new DeliveryCoverageEntryDto("UA-32", null) },
            new[] { "UA-32" },
            null);

        var errors = DeliveryCoverageJson.Validate(dto);

        Assert.Contains(errors, e => e.Contains("UA-32") && e.Contains("одночасно"));
    }

    [Fact]
    public void Validate_EmptyCoverage_ReturnsNoErrors()
    {
        var dto = new DeliveryCoverageDto(Array.Empty<DeliveryCoverageEntryDto>(), Array.Empty<string>(), null);
        Assert.Empty(DeliveryCoverageJson.Validate(dto));
    }
}
