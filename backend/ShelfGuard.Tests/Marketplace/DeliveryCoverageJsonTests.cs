using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-650 / TASK-665: the shared (de)serialization + validation helper for the
/// <c>supplier_profiles.DeliveryCoverage</c> JSONB string, restructured in TASK-665 to
/// per-region structured delivery fields (day range, min order amount, note).
/// </summary>
public sealed class DeliveryCoverageJsonTests
{
    private static DeliveryCoverageEntryDto Entry(
        string code, int? min = null, int? max = null, decimal? minOrder = null, string? note = null) =>
        new(code, min, max, minOrder, note);

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
            """{"served":[{"regionCode":"UA-32","deliveryDaysMin":1,"deliveryDaysMax":3,"minOrderAmount":5000,"note":"Новою Поштою"}],"notServed":["UA-43"],"note":"Загальна"}""";

        var dto = DeliveryCoverageJson.Parse(json);

        Assert.NotNull(dto);
        var served = Assert.Single(dto!.Served);
        Assert.Equal("UA-32", served.RegionCode);
        Assert.Equal(1, served.DeliveryDaysMin);
        Assert.Equal(3, served.DeliveryDaysMax);
        Assert.Equal(5000m, served.MinOrderAmount);
        Assert.Equal("Новою Поштою", served.Note);
        Assert.Equal(new[] { "UA-43" }, dto.NotServed);
        Assert.Equal("Загальна", dto.Note);
    }

    [Fact]
    public void Parse_LegacyTermsField_MapsIntoNote_AndIsNeverWrittenBack()
    {
        // Pre-TASK-665 dev-DB rows stored the single free-text field as "terms".
        const string legacy =
            """{"served":[{"regionCode":"UA-32","terms":"2-3 дні, від 5000 грн"}],"notServed":[],"note":null}""";

        var dto = DeliveryCoverageJson.Parse(legacy);
        Assert.NotNull(dto);
        var served = Assert.Single(dto!.Served);
        Assert.Equal("2-3 дні, від 5000 грн", served.Note);
        Assert.Null(served.DeliveryDaysMin);
        Assert.Null(served.DeliveryDaysMax);
        Assert.Null(served.MinOrderAmount);

        // Re-serialize: no "terms" key, the healed value now lives in "note".
        var json = DeliveryCoverageJson.Serialize(dto);
        Assert.DoesNotContain("terms", json);
        var back = DeliveryCoverageJson.Parse(json);
        Assert.Equal("2-3 дні, від 5000 грн", back!.Served[0].Note);
    }

    [Fact]
    public void Parse_LegacyTerms_DoesNotOverrideAnExplicitNote()
    {
        const string json =
            """{"served":[{"regionCode":"UA-32","terms":"legacy","note":"explicit"}],"notServed":[],"note":null}""";

        var dto = DeliveryCoverageJson.Parse(json);
        Assert.Equal("explicit", dto!.Served[0].Note);
    }

    [Fact]
    public void Parse_ToleratesMissingKeys()
    {
        var dto = DeliveryCoverageJson.Parse("""{"served":[{"regionCode":"UA-30"}]}""");

        Assert.NotNull(dto);
        Assert.Equal("UA-30", dto!.Served[0].RegionCode);
        Assert.Null(dto.Served[0].Note);
        Assert.Null(dto.Served[0].DeliveryDaysMin);
        Assert.Empty(dto.NotServed);
        Assert.Null(dto.Note);
    }

    [Fact]
    public void Parse_NormalizesWhitespaceAndDropsBlanks()
    {
        const string json =
            """{"served":[{"regionCode":" UA-32 ","note":"  "},{"regionCode":""}],"notServed":["  ","UA-43"],"note":"  "}""";

        var dto = DeliveryCoverageJson.Parse(json);

        Assert.NotNull(dto);
        var served = Assert.Single(dto!.Served);
        Assert.Equal("UA-32", served.RegionCode);
        Assert.Null(served.Note);                    // whitespace-only note → null
        Assert.Equal(new[] { "UA-43" }, dto.NotServed);
        Assert.Null(dto.Note);
    }

    [Fact]
    public void Parse_ReversedDayRange_IsSwappedToAscending()
    {
        var dto = DeliveryCoverageJson.Parse(
            """{"served":[{"regionCode":"UA-32","deliveryDaysMin":7,"deliveryDaysMax":2}],"notServed":[],"note":null}""");

        Assert.Equal(2, dto!.Served[0].DeliveryDaysMin);
        Assert.Equal(7, dto.Served[0].DeliveryDaysMax);
    }

    // ── Serialize ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesCamelCase_AndParseRoundTrips()
    {
        var dto = new DeliveryCoverageDto(
            new[] { Entry("UA-32", min: 1, max: 2, minOrder: 5000m, note: "від 5000 грн") },
            new[] { "UA-40" },
            "note");

        var json = DeliveryCoverageJson.Serialize(dto);

        Assert.Contains("\"served\"", json);
        Assert.Contains("\"regionCode\"", json);
        Assert.Contains("\"deliveryDaysMin\"", json);
        Assert.Contains("\"minOrderAmount\"", json);
        Assert.Contains("\"notServed\"", json);
        Assert.DoesNotContain("\"terms\"", json);

        var back = DeliveryCoverageJson.Parse(json);
        Assert.NotNull(back);
        Assert.Equal("UA-32", back!.Served[0].RegionCode);
        Assert.Equal(1, back.Served[0].DeliveryDaysMin);
        Assert.Equal(2, back.Served[0].DeliveryDaysMax);
        Assert.Equal(5000m, back.Served[0].MinOrderAmount);
        Assert.Equal("від 5000 грн", back.Served[0].Note);
        Assert.Equal(new[] { "UA-40" }, back.NotServed);
        Assert.Equal("note", back.Note);
    }

    [Fact]
    public void Serialize_OmitsNullStructuredFields()
    {
        var json = DeliveryCoverageJson.Serialize(new DeliveryCoverageDto(
            new[] { Entry("UA-32") }, Array.Empty<string>(), null));

        Assert.DoesNotContain("deliveryDaysMin", json);
        Assert.DoesNotContain("minOrderAmount", json);
        Assert.DoesNotContain("\"note\"", json);
    }

    [Fact]
    public void Serialize_DedupesServedByRegionCode_KeepingFirstWithItsFields()
    {
        var dto = new DeliveryCoverageDto(
            new[]
            {
                Entry("UA-32", min: 1, max: 1, note: "first"),
                Entry("UA-32", min: 9, max: 9, note: "second"),
            },
            new[] { "UA-43", "UA-43" },
            null);

        var back = DeliveryCoverageJson.Parse(DeliveryCoverageJson.Serialize(dto));

        Assert.NotNull(back);
        var served = Assert.Single(back!.Served);
        Assert.Equal("first", served.Note);
        Assert.Equal(1, served.DeliveryDaysMin);
        Assert.Equal(new[] { "UA-43" }, back.NotServed);
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllKnownCodes_NoOverlap_InRange_ReturnsNoErrors()
    {
        var dto = new DeliveryCoverageDto(
            new[]
            {
                Entry("UA-32", min: 0, max: 365, minOrder: 0m),
                Entry("UA-18-ZHYTOMYR", note: "х"),
            },
            new[] { "UA-43" },
            "note");

        Assert.Empty(DeliveryCoverageJson.Validate(dto));
    }

    [Fact]
    public void Validate_UnknownRegionCode_ReturnsError()
    {
        var served = new DeliveryCoverageDto(
            new[] { Entry("UA-999") }, Array.Empty<string>(), null);
        var notServed = new DeliveryCoverageDto(
            Array.Empty<DeliveryCoverageEntryDto>(), new[] { "NOPE" }, null);

        Assert.NotEmpty(DeliveryCoverageJson.Validate(served));
        Assert.NotEmpty(DeliveryCoverageJson.Validate(notServed));
    }

    [Fact]
    public void Validate_CodeInBothServedAndNotServed_ReturnsError()
    {
        var dto = new DeliveryCoverageDto(
            new[] { Entry("UA-32") },
            new[] { "UA-32" },
            null);

        var errors = DeliveryCoverageJson.Validate(dto);

        Assert.Contains(errors, e => e.Contains("UA-32") && e.Contains("одночасно"));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -5)]
    [InlineData(null, 400)]
    [InlineData(366, null)]
    public void Validate_DeliveryDaysOutOfRange_ReturnsError(int? min, int? max)
    {
        var dto = new DeliveryCoverageDto(
            new[] { Entry("UA-32", min: min, max: max) }, Array.Empty<string>(), null);

        Assert.Contains(DeliveryCoverageJson.Validate(dto), e => e.Contains("Термін доставки"));
    }

    [Fact]
    public void Validate_NegativeMinOrderAmount_ReturnsError()
    {
        var dto = new DeliveryCoverageDto(
            new[] { Entry("UA-32", minOrder: -1m) }, Array.Empty<string>(), null);

        Assert.Contains(DeliveryCoverageJson.Validate(dto), e => e.Contains("Мінімальна сума"));
    }

    [Fact]
    public void Validate_ReversedButInRangeDayPair_IsSwappedThenPasses()
    {
        var dto = new DeliveryCoverageDto(
            new[] { Entry("UA-32", min: 10, max: 3) }, Array.Empty<string>(), null);

        Assert.Empty(DeliveryCoverageJson.Validate(dto));
    }

    [Fact]
    public void Validate_EmptyCoverage_ReturnsNoErrors()
    {
        var dto = new DeliveryCoverageDto(Array.Empty<DeliveryCoverageEntryDto>(), Array.Empty<string>(), null);
        Assert.Empty(DeliveryCoverageJson.Validate(dto));
    }
}
