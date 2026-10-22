using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Locations;

public sealed class ShelfInsightTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0];

    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IShelfRepository _repo = Substitute.For<IShelfRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Location _location;
    private readonly LocationZone _zone;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "floorplan-tests-" + Guid.NewGuid().ToString("N"));

    public ShelfInsightTests()
    {
        _location = new Location { TenantId = _tenantId, Name = "Shop" };
        _zone = new LocationZone { LocationId = _location.Id, Name = "Z", Location = _location };
        _locations.GetZoneWithLocationAsync(_zone.Id, Arg.Any<CancellationToken>()).Returns(_zone);
        _locations.GetByIdAsync(_location.Id, Arg.Any<CancellationToken>()).Returns(_location);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ShelfInsightService Insights() => new(_locations, _repo) { Today = () => Today };

    private static ShelfItem Row(Guid itemId, int facings = 4) =>
        new() { ItemId = itemId, ExpectedFacings = facings, Item = new Item { Name = "Milk", Barcodes = ["111"] } };

    // -- insights ---------------------------------------------------------

    [Fact]
    public async Task Insights_BulkQueries_AggregateShelfStoreWarehouse_NoNPlusOne()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var otherZone = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
        var shelfId = Guid.NewGuid();
        _repo.GetShelfItemsAsync(_zone.Id, shelfId, Arg.Any<CancellationToken>()).Returns([Row(a), Row(b, 2)]);
        _repo.GetStockAggregatesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(
        [
            new ShelfStockAggregate(a, _location.Id, _zone.Id, "retail_store", 5, new DateOnly(2026, 11, 1)),
            new ShelfStockAggregate(a, _location.Id, otherZone, "retail_store", 7, new DateOnly(2026, 10, 25)),
            new ShelfStockAggregate(a, warehouse, null, "warehouse", 40, new DateOnly(2026, 9, 1)),
        ]);
        _repo.GetSalesAggregatesAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Today, Arg.Any<CancellationToken>())
            .Returns([new ShelfSalesAggregate(a, 14, 7, 60)]);
        _repo.GetAduAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [a] = 3m });

        var (items, error) = await Insights().GetShelfInsightsAsync(_tenantId, _location.Id, _zone.Id, shelfId);

        Assert.Null(error);
        Assert.Equal(2, items!.Count);
        var ia = items.Single(i => i.ItemId == a);
        Assert.Equal(5, ia.OnShelfQty);
        Assert.Equal(12, ia.StoreQty);
        Assert.Equal(40, ia.WarehouseQty);
        Assert.Equal(new DateOnly(2026, 10, 25), ia.NearestExpiry); // store FEFO, warehouse excluded
        Assert.Equal(14, ia.Sold7d);
        Assert.Equal(60, ia.Sold30d);
        Assert.Equal(3m, ia.Adu);
        Assert.Equal(4m, ia.DaysOfCover);   // 12 / 3
        Assert.Equal(100m, ia.TrendPct);    // 14 vs 7
        Assert.Equal("111", ia.Barcode);

        var ib = items.Single(i => i.ItemId == b);
        Assert.Equal(0, ib.StoreQty);
        Assert.Null(ib.NearestExpiry);
        Assert.Null(ib.TrendPct);
        Assert.Null(ib.DaysOfCover);

        // one bulk query per concern regardless of item count
        await _repo.Received(1).GetStockAggregatesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).GetSalesAggregatesAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Today, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetAduAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Insights_NoCachedAdu_FallsBackToThirtyDayAverage()
    {
        var a = Guid.NewGuid();
        _repo.GetStockAggregatesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(
            [new ShelfStockAggregate(a, _location.Id, _zone.Id, "retail_store", 6, Today)]);
        _repo.GetSalesAggregatesAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Today, Arg.Any<CancellationToken>())
            .Returns([new ShelfSalesAggregate(a, 0, 0, 30)]);
        _repo.GetAduAsync(_location.Id, Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());

        var list = await Insights().BuildAsync(_location.Id, _zone.Id, [Row(a)]);

        Assert.Equal(1m, list[0].Adu);
        Assert.Equal(6m, list[0].DaysOfCover);
    }

    [Fact]
    public async Task Insights_OtherTenant_NotFound_NoQueries()
    {
        var (items, error) = await Insights().GetShelfInsightsAsync(Guid.NewGuid(), _location.Id, _zone.Id, Guid.NewGuid());

        Assert.Null(items);
        Assert.Equal("Zone not found.", error);
        await _repo.DidNotReceiveWithAnyArgs().GetStockAggregatesAsync(default!, default);
    }

    // -- enrichment rules -------------------------------------------------

    private static ShelfItemInsightDto Ins(Guid id, decimal onShelf = 0, decimal store = 0, decimal wh = 0,
        decimal s7 = 0, decimal s30 = 0, decimal adu = 0, decimal? cover = null, int facings = 4) =>
        new(id, "Milk", null, null, facings, onShelf, store, wh, null, s7, s30, adu, cover, null);

    private static ShelfScanItemDto Vis(Guid id, string status, int count = 0, double conf = 0.9) =>
        new(id, "Milk", status, count, conf, null);

    [Fact]
    public void Rules_WarehouseHasStock_RestockCappedByWarehouse()
    {
        var id = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich([Vis(id, "missing")], [Ins(id, wh: 2, adu: 1, s7: 10)]);

        var rec = res[0].Recommendation!;
        Assert.Equal("restock_from_warehouse", rec.Action);
        Assert.Equal(2, rec.SuggestedQty); // need 4, warehouse only 2
        Assert.Equal("high", rec.Priority); // missing + sells >= 7/week
        Assert.Equal(2, res[0].WarehouseQty);
    }

    [Fact]
    public void Rules_LowItem_SuggestsMissingFacingsOrTwoDaysDemand()
    {
        var id = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich([Vis(id, "low", count: 1)], [Ins(id, wh: 100, adu: 5, s7: 3, s30: 20)]);

        var rec = res[0].Recommendation!;
        Assert.Equal("restock_from_warehouse", rec.Action);
        Assert.Equal(10, rec.SuggestedQty); // max(4-1, ceil(5*2))
        Assert.Equal("low", rec.Priority);
    }

    [Fact]
    public void Rules_NoWarehouseStock_OrderFromSupplier()
    {
        var id = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich([Vis(id, "missing")], [Ins(id, s7: 2, s30: 8, adu: 0.3m)]);

        var rec = res[0].Recommendation!;
        Assert.Equal("order_from_supplier", rec.Action);
        Assert.Equal(4, rec.SuggestedQty);
        Assert.Equal("medium", rec.Priority);
    }

    [Fact]
    public void Rules_NeverSold_MissingDowngradedToLowPriority()
    {
        var id = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich([Vis(id, "missing")], [Ins(id)]);
        Assert.Equal("low", res[0].Recommendation!.Priority);
    }

    [Fact]
    public void Rules_LowConfidenceOrSystemSaysOnShelf_CheckManually()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich(
            [Vis(a, "missing", conf: 0.1), Vis(b, "missing")], [Ins(a), Ins(b, onShelf: 3)]);

        Assert.All(res, r => Assert.Equal("check_manually", r.Recommendation!.Action));
        Assert.All(res, r => Assert.Equal(0, r.Recommendation!.SuggestedQty));
    }

    [Fact]
    public void Rules_PresentItem_GetsQuantitiesButNoAction()
    {
        var id = Guid.NewGuid();
        var res = ShelfRecommendationRules.Enrich([Vis(id, "present", 5)], [Ins(id, onShelf: 5, store: 9, wh: 3)]);

        Assert.Equal("none", res[0].Recommendation!.Action);
        Assert.Equal(9, res[0].StoreQty);
    }

    // -- scan enrichment through the service ------------------------------

    private (ShelfService Sut, IShelfInsightService Ins, IShelfRestockAdvisor Restock) Scanner()
    {
        var advisor = Substitute.For<IShelfPhotoAdvisor>();
        var ins = Substitute.For<IShelfInsightService>();
        var restock = Substitute.For<IShelfRestockAdvisor>();
        advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        var milk = Guid.NewGuid();
        _repo.GetShelfItemsAsync(_zone.Id, null, Arg.Any<CancellationToken>()).Returns([Row(milk)]);
        advisor.AnalyzeAsync(default!, default!, default!, default).ReturnsForAnyArgs(new ShelfPhotoAnalysis(
            [new ShelfPhotoItemVerdict(milk, "Milk", "missing", 0, 0.9, null)], "vision summary", "m", 1));
        ins.BuildAsync(default, default, default!, default)
            .ReturnsForAnyArgs(new List<ShelfItemInsightDto> { Ins(milk, wh: 10, s7: 8, adu: 1) });
        var sut = new ShelfService(_locations, _repo, advisor, ins, restock, NullLogger<ShelfService>.Instance)
        {
            UploadRoot = Path.Combine(_root, "scans"),
            FloorPlanRoot = Path.Combine(_root, "plans"),
        };
        return (sut, ins, restock);
    }

    private Task<(ShelfScanDto? Scan, string? Error, int? StatusCode)> RunScan(ShelfService sut) =>
        sut.CreateScanAsync(_tenantId, null, _location.Id, _zone.Id, null, new MemoryStream(Jpeg), Jpeg.Length, "p.jpg");

    [Fact]
    public async Task CreateScan_EnrichesMissingItem_AndUsesAiSummary()
    {
        var (sut, _, restock) = Scanner();
        restock.SummarizeAsync(default!, default).ReturnsForAnyArgs("AI summary");
        ShelfScan? saved = null;
        await _repo.AddScanAsync(Arg.Do<ShelfScan>(s => saved = s), Arg.Any<CancellationToken>());

        var (scan, error, _) = await RunScan(sut);

        Assert.Null(error);
        Assert.Equal("AI summary", scan!.Summary);
        var item = Assert.Single(scan.Items);
        Assert.Equal("restock_from_warehouse", item.Recommendation!.Action);
        Assert.Equal(10, item.WarehouseQty);
        Assert.Contains("restock_from_warehouse", saved!.ResultJson); // snapshot persisted
    }

    [Fact]
    public async Task CreateScan_RestockAdvisorThrows_ScanStillSucceeds_WithVisionSummary()
    {
        var (sut, _, restock) = Scanner();
        restock.SummarizeAsync(default!, default)
            .ReturnsForAnyArgs<Task<string?>>(_ => throw new InvalidOperationException("down"));

        var (scan, error, status) = await RunScan(sut);

        Assert.Null(error);
        Assert.Null(status);
        Assert.Equal("vision summary", scan!.Summary);
        Assert.NotNull(scan.Items[0].Recommendation);
    }

    [Fact]
    public async Task CreateScan_InsightsThrow_ScanStillSucceeds_WithoutRecommendation()
    {
        var (sut, ins, _) = Scanner();
        ins.BuildAsync(default, default, default!, default)
            .ReturnsForAnyArgs<Task<List<ShelfItemInsightDto>>>(_ => throw new InvalidOperationException("db"));

        var (scan, error, _) = await RunScan(sut);

        Assert.Null(error);
        Assert.Null(scan!.Items[0].Recommendation);
    }

    // -- floor-plan upload validation -------------------------------------

    [Fact]
    public async Task Upload_ValidPng_SavedUnderTenantFolder_WithGeneratedName()
    {
        var (sut, _, _) = Scanner();

        var (url, error, status) = await sut.SaveFloorPlanBackgroundAsync(
            _tenantId, _location.Id, new MemoryStream(Png), Png.Length, "../../evil name.PNG");

        Assert.Null(error);
        Assert.Null(status);
        Assert.StartsWith($"/uploads/floor-plans/{_tenantId}/", url);
        Assert.EndsWith(".png", url);
        Assert.DoesNotContain("evil", url);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "plans", _tenantId.ToString())));
    }

    [Theory]
    [InlineData("plan.pdf")]
    [InlineData("plan.svg")]
    [InlineData("plan")]
    public async Task Upload_BadExtension_400(string name)
    {
        var (sut, _, _) = Scanner();
        var (url, _, status) = await sut.SaveFloorPlanBackgroundAsync(_tenantId, _location.Id, new MemoryStream(Png), Png.Length, name);
        Assert.Null(url);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Upload_ContentNotMatchingExtension_400()
    {
        var (sut, _, _) = Scanner();
        var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };
        var (_, _, status) = await sut.SaveFloorPlanBackgroundAsync(_tenantId, _location.Id, new MemoryStream(junk), junk.Length, "p.png");
        Assert.Equal(400, status);
        Assert.False(Directory.Exists(Path.Combine(_root, "plans")));
    }

    [Fact]
    public async Task Upload_TooLarge_400()
    {
        var (sut, _, _) = Scanner();
        var (_, error, status) = await sut.SaveFloorPlanBackgroundAsync(
            _tenantId, _location.Id, new MemoryStream(Png), ShelfService.MaxFloorPlanBytes + 1, "p.png");
        Assert.Equal(400, status);
        Assert.Contains("8 MB", error);
    }

    [Fact]
    public async Task Upload_OtherTenantLocation_404()
    {
        var (sut, _, _) = Scanner();
        var (_, _, status) = await sut.SaveFloorPlanBackgroundAsync(Guid.NewGuid(), _location.Id, new MemoryStream(Png), Png.Length, "p.png");
        Assert.Equal(404, status);
    }
}
