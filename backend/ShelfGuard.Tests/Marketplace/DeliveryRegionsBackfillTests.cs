using ShelfGuard.Application.Features.Marketplace;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-661 (T14): pure transform behind the one-shot
/// <c>supplier_profiles.DeliveryRegions -> DeliveryCoverage</c> backfill. The free-text →
/// region-code matching itself is covered by <c>UkraineRegionsTests</c>; these tests pin the
/// backfill's own shaping rules (served dedupe, note assembly, note-only / empty results).
/// </summary>
public sealed class DeliveryRegionsBackfillTests
{
    [Fact]
    public void Build_MixedRegions_MapsKnownAndPutsUnmatchedInNote()
    {
        var result = DeliveryRegionsBackfill.Build(
            new[] { "Київська область", "по домовленості", "Житомир" });

        Assert.NotNull(result.Coverage);
        Assert.Equal(new[] { "UA-32", "UA-18-ZHYTOMYR" },
            result.Coverage!.Served.Select(e => e.RegionCode));
        Assert.All(result.Coverage.Served, e =>
        {
            Assert.Null(e.DeliveryDaysMin);
            Assert.Null(e.DeliveryDaysMax);
            Assert.Null(e.MinOrderAmount);
            Assert.Null(e.Note);
        });
        Assert.Empty(result.Coverage.NotServed);
        Assert.Equal("Також: по домовленості", result.Coverage.Note);

        Assert.Equal(new[] { "UA-32", "UA-18-ZHYTOMYR" }, result.MatchedCodes);
        Assert.Equal(new[] { "по домовленості" }, result.Unmatched);
    }

    [Fact]
    public void Build_AllMatch_ProducesNoNote()
    {
        var result = DeliveryRegionsBackfill.Build(new[] { "Одеська область", "Львів" });

        Assert.NotNull(result.Coverage);
        Assert.Equal(new[] { "UA-51", "UA-46-LVIV" },
            result.Coverage!.Served.Select(e => e.RegionCode));
        Assert.Null(result.Coverage.Note);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Build_NoneMatch_ReturnsNoteOnlyCoverage()
    {
        var result = DeliveryRegionsBackfill.Build(new[] { "Вся Україна", "по всій країні" });

        Assert.NotNull(result.Coverage);
        Assert.Empty(result.Coverage!.Served);
        Assert.Empty(result.Coverage.NotServed);
        Assert.Equal("Також: Вся Україна, по всій країні", result.Coverage.Note);
        Assert.Empty(result.MatchedCodes);
    }

    [Fact]
    public void Build_DedupesServedByCode_FirstOccurrenceWins()
    {
        var result = DeliveryRegionsBackfill.Build(
            new[] { "Київська область", "Київська обл.", "київська" });

        Assert.NotNull(result.Coverage);
        Assert.Equal(new[] { "UA-32" }, result.Coverage!.Served.Select(e => e.RegionCode));
        Assert.Null(result.Coverage.Note);
    }

    [Fact]
    public void Build_DedupesUnmatchedCaseInsensitively()
    {
        var result = DeliveryRegionsBackfill.Build(new[] { "Вся Україна", "вся україна", "  Вся Україна  " });

        Assert.NotNull(result.Coverage);
        Assert.Equal("Також: Вся Україна", result.Coverage!.Note);
    }

    [Fact]
    public void Build_EmptyArray_ReturnsNullCoverage()
    {
        var result = DeliveryRegionsBackfill.Build(Array.Empty<string>());

        Assert.Null(result.Coverage);
        Assert.Empty(result.MatchedCodes);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void Build_OnlyBlankEntries_ReturnsNullCoverage()
    {
        var result = DeliveryRegionsBackfill.Build(new[] { "", "   ", "\t" });

        Assert.Null(result.Coverage);
    }

    [Fact]
    public void Build_NullInput_ReturnsNullCoverage()
    {
        var result = DeliveryRegionsBackfill.Build(null);

        Assert.Null(result.Coverage);
    }

    [Fact]
    public void Build_Output_SerializesToValidCoverageJsonAndRoundTrips()
    {
        var result = DeliveryRegionsBackfill.Build(new[] { "Одеська область", "невідомий регіон" });

        var json = DeliveryCoverageJson.Serialize(result.Coverage!);
        Assert.Empty(DeliveryCoverageJson.Validate(result.Coverage!));

        var parsed = DeliveryCoverageJson.Parse(json);
        Assert.NotNull(parsed);
        Assert.Equal("UA-51", parsed!.Served.Single().RegionCode);
        Assert.Equal("Також: невідомий регіон", parsed.Note);
    }
}
