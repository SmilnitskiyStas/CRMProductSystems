using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Promotions;

public sealed class StoreTimeZoneTests
{
    [Fact]
    public void Kyiv_resolves_on_this_platform_with_dst()
    {
        var tz = StoreTimeZones.Resolve("Europe/Kyiv");
        Assert.NotNull(tz);
        Assert.Equal(TimeSpan.FromHours(3), tz!.GetUtcOffset(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromHours(2), tz.GetUtcOffset(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        Assert.NotNull(StoreTimeZones.Resolve("Europe/Kiev")); // legacy alias
        Assert.Null(StoreTimeZones.Resolve("Mars/Olympus"));
        Assert.Null(StoreTimeZones.Resolve("  "));
        Assert.Null(StoreTimeZones.Resolve(null));
    }

    // ── LocationService validation ─────────────────────────────────────────

    private readonly ILocationRepository _repo = Substitute.For<ILocationRepository>();

    private LocationService Svc() =>
        new(_repo, Substitute.For<IUserLocationRepository>(), Substitute.For<IGeocodingClient>());

    [Fact]
    public async Task Create_defaults_to_kyiv_accepts_valid_and_rejects_unknown_zone()
    {
        var tenant = Guid.NewGuid();
        var (def, e1) = await Svc().CreateAsync(tenant, new CreateLocationRequest("S", null, null, null, "retail_store"));
        Assert.Null(e1);
        Assert.Equal("Europe/Kyiv", def!.TimeZone);

        var (utc, e2) = await Svc().CreateAsync(tenant, new CreateLocationRequest("S", null, null, null, "retail_store", TimeZone: " Europe/Warsaw "));
        Assert.Null(e2);
        Assert.Equal("Europe/Warsaw", utc!.TimeZone);

        var (bad, e3) = await Svc().CreateAsync(tenant, new CreateLocationRequest("S", null, null, null, "retail_store", TimeZone: "Mars/Olympus"));
        Assert.Null(bad);
        Assert.Contains("time zone", e3);
    }

    [Fact]
    public async Task Update_keeps_the_zone_on_blank_changes_it_when_valid_and_rejects_unknown()
    {
        var loc = new Location { TenantId = Guid.NewGuid(), Name = "S", TimeZone = "Europe/Warsaw" };
        _repo.GetByIdAsync(loc.Id, Arg.Any<CancellationToken>()).Returns(loc);
        UpdateLocationRequest Req(string? tz) => new("S", null, null, null, "retail_store", true, TimeZone: tz);

        Assert.Null((await Svc().UpdateAsync(loc.Id, Req(null))).Error);
        Assert.Equal("Europe/Warsaw", loc.TimeZone);
        Assert.Null((await Svc().UpdateAsync(loc.Id, Req(""))).Error);
        Assert.Equal("Europe/Warsaw", loc.TimeZone);
        var (dto, error) = await Svc().UpdateAsync(loc.Id, Req("Europe/Kyiv"));
        Assert.Null(error);
        Assert.Equal(("Europe/Kyiv", "Europe/Kyiv"), (loc.TimeZone, dto!.TimeZone));

        var (bad, e) = await Svc().UpdateAsync(loc.Id, Req("Nope/Zone"));
        Assert.Null(bad);
        Assert.Contains("time zone", e);
        Assert.Equal("Europe/Kyiv", loc.TimeZone);
    }

    // ── sync response + etag ───────────────────────────────────────────────

    [Fact]
    public async Task Sync_promotions_returns_the_store_zone_and_the_etag_changes_with_it()
    {
        var tenant = Guid.NewGuid();
        var store = new Location { TenantId = tenant, Name = "S", TimeZone = "Europe/Kyiv" };
        var locations = Substitute.For<ILocationRepository>();
        locations.GetByIdAsync(store.Id, Arg.Any<CancellationToken>()).Returns(store);
        var rules = Substitute.For<IPromotionRuleRepository>();
        rules.GetPublishedAsync(tenant, Arg.Any<CancellationToken>()).Returns(
            [new PublishedPromotionRuleRow(Guid.NewGuid(), "r", DateTime.UtcNow, Guid.NewGuid(),
                """{"type":"percent_off","targets":{"itemIds":["00000000-0000-0000-0000-00000000000a"]},"params":{"percent":10}}""")]);
        var sync = new PosSyncService(Substitute.For<ICatalogSyncRepository>(), rules, null, locations);

        var (first, err, _) = await sync.GetPromotionsAsync(tenant, null, store.Id, null);
        Assert.Null(err);
        Assert.Equal("Europe/Kyiv", first!.StoreTimeZone);
        Assert.Single(first.Rules);

        var (same, _, _) = await sync.GetPromotionsAsync(tenant, null, store.Id, first.Etag);
        Assert.True(same!.NotModified);
        Assert.Equal("Europe/Kyiv", same.StoreTimeZone);

        store.TimeZone = "Europe/Warsaw";
        var (changed, _, _) = await sync.GetPromotionsAsync(tenant, null, store.Id, first.Etag);
        Assert.False(changed!.NotModified);
        Assert.NotEqual(first.Etag, changed.Etag);
        Assert.Equal("Europe/Warsaw", changed.StoreTimeZone);
    }

    // ── engine ─────────────────────────────────────────────────────────────

    [Fact]
    public void Engine_uses_the_store_zone_over_the_legacy_offset_and_ignores_unknown_zones()
    {
        var item = Guid.NewGuid();
        var def = PromotionDefinitionJson.TryParse(
            $$$"""{"type":"percent_off","targets":{"itemIds":["{{{item}}}"]},"params":{"percent":50},"scope":{"timeFrom":"09:00","timeTo":"10:00"}}""")!;
        var rule = new PromotionRuleInput(Guid.NewGuid(), Guid.NewGuid(), "r", DateTimeOffset.UtcNow, def);
        var at = new DateTimeOffset(2026, 7, 15, 6, 30, 0, TimeSpan.Zero); // 09:30 Kyiv
        decimal Price(PromotionContext c) => PromotionEngine.Evaluate(
            [new PromotionLineInput(item, null, 10m, 1m)], [rule], c).Lines[0].Segments[0].UnitPrice;

        Assert.Equal(5m, Price(new PromotionContext(Guid.NewGuid(), at, 0, null, "Europe/Kyiv")));
        Assert.Equal(10m, Price(new PromotionContext(Guid.NewGuid(), at, 0, null, null)));
        Assert.Equal(10m, Price(new PromotionContext(Guid.NewGuid(), at, 0, null, "Bad/Zone")));
        Assert.Equal(5m, Price(new PromotionContext(Guid.NewGuid(), at, 180, null, "Bad/Zone"))); // legacy offset fallback
        Assert.Equal(10m, Price(new PromotionContext(Guid.NewGuid(), at, 180, null, "Europe/London"))); // zone wins over offset
    }
}
