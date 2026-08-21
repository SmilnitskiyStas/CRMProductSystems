using System.Security.Claims;
using NSubstitute;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Analytics;

public sealed class PosAnalyticsServiceTests
{
    private readonly IAnalyticsRepository _repo = Substitute.For<IAnalyticsRepository>();
    private readonly IStockStatusSnapshotRepository _snapshots = Substitute.For<IStockStatusSnapshotRepository>();
    private readonly AnalyticsService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly From30  = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

    public PosAnalyticsServiceTests() => _sut = new AnalyticsService(_repo, _snapshots);

    // ── GetPosSummary ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPosSummaryAsync_delegates_to_repository()
    {
        var expected = new PosAnalyticsSummaryDto(
            TotalRevenue: 0, TransactionCount: 0, AverageTicket: 0,
            CashRevenue: 0, CardRevenue: 0, ShiftCount: 0,
            From: From30, To: Today);

        _repo.GetPosSummaryAsync(_tenantId, null, From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetPosSummaryAsync(_tenantId, null, From30, Today);

        Assert.Equal(expected, result);
        await _repo.Received(1).GetPosSummaryAsync(_tenantId, null, From30, Today, default);
    }

    [Fact]
    public async Task GetPosSummaryAsync_returns_zero_totals_for_empty_tenant()
    {
        var empty = new PosAnalyticsSummaryDto(
            TotalRevenue: 0, TransactionCount: 0, AverageTicket: 0,
            CashRevenue: 0, CardRevenue: 0, ShiftCount: 0,
            From: From30, To: Today);

        _repo.GetPosSummaryAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
             .Returns(empty);

        var result = await _sut.GetPosSummaryAsync(Guid.NewGuid(), null, From30, Today);

        Assert.Equal(0, result.TotalRevenue);
        Assert.Equal(0, result.TransactionCount);
        Assert.Equal(0, result.AverageTicket);
        Assert.Equal(0, result.ShiftCount);
    }

    // ── GetPosRevenueTrend ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPosRevenueTrendAsync_delegates_to_repository()
    {
        var expected = new PosRevenueTrendDto(Points: [], GroupBy: "day");

        _repo.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day", default)
             .Returns(expected);

        var result = await _sut.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day");

        Assert.Equal("day", result.GroupBy);
        await _repo.Received(1).GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day", default);
    }

    [Fact]
    public async Task GetPosRevenueTrendAsync_week_groupBy_passes_week_to_repository()
    {
        var expected = new PosRevenueTrendDto(Points: [], GroupBy: "week");

        _repo.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week", default)
             .Returns(expected);

        var result = await _sut.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week");

        Assert.Equal("week", result.GroupBy);
        await _repo.Received(1).GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week", default);
    }

    // ── GetPosTopProducts ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPosTopProductsAsync_returns_items_sorted_by_revenue_descending()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        var expected = new PosTopProductsDto(Items: new List<TopProductDto>
        {
            new(productB, "Milk",  "1111111", TotalRevenue: 500m, TotalQuantity: 50, TransactionCount: 20),
            new(productA, "Bread", "2222222", TotalRevenue: 300m, TotalQuantity: 30, TransactionCount: 10),
        });

        _repo.GetPosTopProductsAsync(_tenantId, null, From30, Today, 10, default)
             .Returns(expected);

        var result = await _sut.GetPosTopProductsAsync(_tenantId, null, From30, Today, 10);

        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].TotalRevenue >= result.Items[1].TotalRevenue,
            "Items should be ordered by revenue descending");
        Assert.Equal(productB, result.Items[0].ProductId);
    }

    [Fact]
    public async Task GetPosTopProductsAsync_empty_period_returns_empty_list()
    {
        var expected = new PosTopProductsDto(Items: []);

        _repo.GetPosTopProductsAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var result = await _sut.GetPosTopProductsAsync(Guid.NewGuid(), null, From30, Today, 10);

        Assert.Empty(result.Items);
    }

    // ── GetPosCashierStats ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPosCashierStatsAsync_delegates_to_repository()
    {
        var expected = new PosCashierStatsDto(Cashiers: []);

        _repo.GetPosCashierStatsAsync(_tenantId, null, From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetPosCashierStatsAsync(_tenantId, null, From30, Today);

        Assert.Empty(result.Cashiers);
        await _repo.Received(1).GetPosCashierStatsAsync(_tenantId, null, From30, Today, default);
    }

    [Fact]
    public async Task GetPosCashierStatsAsync_cashier_average_ticket_is_revenue_over_transaction_count()
    {
        var cashierId = Guid.NewGuid();

        var expected = new PosCashierStatsDto(Cashiers: new List<CashierStatDto>
        {
            new(cashierId, "Ivan Koval", TotalRevenue: 1000m, TransactionCount: 10, AverageTicket: 100m, ShiftCount: 2),
        });

        _repo.GetPosCashierStatsAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var result = await _sut.GetPosCashierStatsAsync(_tenantId, null, From30, Today);

        var cashier = Assert.Single(result.Cashiers);
        Assert.Equal(100m, cashier.AverageTicket);
        Assert.Equal(2, cashier.ShiftCount);
    }

    // ── TASK-481: GetCategoryProductBreakdownAsync ───────────────────────────

    [Fact]
    public async Task GetCategoryProductBreakdownAsync_delegates_to_repository()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var expected = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", Safe: 5, Warning: 1, Critical: 0, Expired: 0, TotalQuantity: 60m,
                    SalesRevenue: 500m, UnitsSold: 50m, MarginAmount: null, MarginPercent: null,
                    DaysOfStockRemaining: null),
            });

        _repo.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, false, default)
             .Returns(expected);

        var result = await _sut.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, includeMargin: false);

        Assert.Equal(categoryId, result.CategoryId);
        Assert.Equal("Dairy", result.CategoryName);
        var row = Assert.Single(result.Products);
        Assert.Equal(productId, row.ProductId);
        Assert.Equal(500m, row.SalesRevenue);
        await _repo.Received(1).GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, false, default);
    }

    [Fact]
    public async Task GetCategoryProductBreakdownAsync_null_category_id_is_forwarded_as_uncategorized_bucket()
    {
        var expected = new CategoryProductBreakdownDto(CategoryId: null, CategoryName: "Без категорії", Products: []);

        _repo.GetCategoryProductBreakdownAsync(_tenantId, null, null, From30, Today, false, default)
             .Returns(expected);

        var result = await _sut.GetCategoryProductBreakdownAsync(_tenantId, null, null, From30, Today, includeMargin: false);

        Assert.Null(result.CategoryId);
        Assert.Equal("Без категорії", result.CategoryName);
        await _repo.Received(1).GetCategoryProductBreakdownAsync(_tenantId, null, null, From30, Today, false, default);
    }

    // Pins ADR-027's authorization contract end to end at this layer: constructs the same
    // ClaimsPrincipal shape AnalyticsAuthorizationTests uses, resolves CanViewMargin exactly as
    // the controller will, and proves the resulting bool is what decides whether the DTO's
    // margin fields come back null or populated -- store_manager clears the base
    // AnalyticsViewOrCapability controller floor but not this narrower, one-tier-higher check;
    // network_manager clears both.
    [Fact]
    public async Task GetCategoryProductBreakdownAsync_margin_is_null_for_store_manager_and_populated_for_network_manager()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var withoutMargin = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", 5, 1, 0, 0, 60m, 500m, 50m, MarginAmount: null, MarginPercent: null, DaysOfStockRemaining: null),
            });

        var withMargin = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", 5, 1, 0, 0, 60m, 500m, 50m, MarginAmount: 150m, MarginPercent: 30m, DaysOfStockRemaining: null),
            });

        _repo.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, false, default)
             .Returns(withoutMargin);
        _repo.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, true, default)
             .Returns(withMargin);

        var storeManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.StoreManager));
        var networkManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.NetworkManager));
        Assert.False(storeManagerCanViewMargin);
        Assert.True(networkManagerCanViewMargin);

        var storeManagerResult = await _sut.GetCategoryProductBreakdownAsync(
            _tenantId, null, categoryId, From30, Today, includeMargin: storeManagerCanViewMargin);
        var networkManagerResult = await _sut.GetCategoryProductBreakdownAsync(
            _tenantId, null, categoryId, From30, Today, includeMargin: networkManagerCanViewMargin);

        Assert.All(storeManagerResult.Products, p => Assert.Null(p.MarginAmount));
        Assert.All(storeManagerResult.Products, p => Assert.Null(p.MarginPercent));
        Assert.All(networkManagerResult.Products, p => Assert.NotNull(p.MarginAmount));
        Assert.All(networkManagerResult.Products, p => Assert.NotNull(p.MarginPercent));

        await _repo.Received(1).GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, false, default);
        await _repo.Received(1).GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, true, default);
    }

    // ── TASK-491: GetCategoryProductBreakdownAsync — DaysOfStockRemaining ──────
    //
    // The division itself (TotalQuantity / ProductAdu.AduEffective) and its store-scope /
    // zero-ADU guards live in AnalyticsRepository.GetCategoryProductBreakdownAsync -- not
    // independently unit-tested anywhere in this codebase, same precedent as
    // GetWorstProductsAsync's stock/sales merge and every other GetXxxAsync repository method in
    // this file (see that section's own comment above). These three pin the DTO shape/pass-through
    // at the service layer: whatever value the repository computes for each of the three
    // null-semantics cases round-trips through AnalyticsService unchanged.

    [Fact]
    public async Task GetCategoryProductBreakdownAsync_days_of_stock_remaining_populated_when_store_scoped()
    {
        var storeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var expected = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", Safe: 5, Warning: 1, Critical: 0, Expired: 0, TotalQuantity: 60m,
                    SalesRevenue: 500m, UnitsSold: 50m, MarginAmount: null, MarginPercent: null,
                    DaysOfStockRemaining: 12.5m),
            });

        _repo.GetCategoryProductBreakdownAsync(_tenantId, storeId, categoryId, From30, Today, false, default)
             .Returns(expected);

        var result = await _sut.GetCategoryProductBreakdownAsync(_tenantId, storeId, categoryId, From30, Today, includeMargin: false);

        var row = Assert.Single(result.Products);
        Assert.Equal(12.5m, row.DaysOfStockRemaining);
        await _repo.Received(1).GetCategoryProductBreakdownAsync(_tenantId, storeId, categoryId, From30, Today, false, default);
    }

    // No store_id on the request -- ProductAdu is per-(product, store), so a network-wide/
    // multi-store rollup has no single meaningful ADU to divide by. Repository is expected to
    // return null (never 0) for every row in this shape; this test confirms the service doesn't
    // coerce or reinterpret that null.
    [Fact]
    public async Task GetCategoryProductBreakdownAsync_days_of_stock_remaining_null_without_store_scope()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var expected = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", 5, 1, 0, 0, 60m, 500m, 50m, MarginAmount: null, MarginPercent: null,
                    DaysOfStockRemaining: null),
            });

        _repo.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, false, default)
             .Returns(expected);

        var result = await _sut.GetCategoryProductBreakdownAsync(_tenantId, null, categoryId, From30, Today, includeMargin: false);

        var row = Assert.Single(result.Products);
        Assert.Null(row.DaysOfStockRemaining);
    }

    // store_id IS present but the product has no ProductAdu row (or AduEffective is 0/null --
    // "no usage history yet", a real valid state). Repository's division-by-zero guard is what
    // produces this null; this test proves resolving it doesn't throw and the null survives the
    // service pass-through untouched, same as the no-store-scope case above.
    [Fact]
    public async Task GetCategoryProductBreakdownAsync_days_of_stock_remaining_null_when_adu_zero_or_missing()
    {
        var storeId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var expected = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "New Item", 0, 0, 0, 0, 10m, 0m, 0m, MarginAmount: null, MarginPercent: null,
                    DaysOfStockRemaining: null),
            });

        _repo.GetCategoryProductBreakdownAsync(_tenantId, storeId, categoryId, From30, Today, false, default)
             .Returns(expected);

        var result = await _sut.GetCategoryProductBreakdownAsync(_tenantId, storeId, categoryId, From30, Today, includeMargin: false);

        var row = Assert.Single(result.Products);
        Assert.Null(row.DaysOfStockRemaining);
    }

    // ── TASK-481: GetLossesByProductAsync ─────────────────────────────────────

    [Fact]
    public async Task GetLossesByProductAsync_delegates_to_repository()
    {
        var productId = Guid.NewGuid();
        var expected = new LossesByProductDto(
            TotalLoss: 200m,
            Products: new List<LossByProductRowDto>
            {
                new(productId, "Bread", Quantity: 10m, LossAmount: 200m, SharePercent: 100m),
            });

        _repo.GetLossesByProductAsync(_tenantId, null, null, From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetLossesByProductAsync(_tenantId, null, null, From30, Today);

        Assert.Equal(200m, result.TotalLoss);
        var row = Assert.Single(result.Products);
        Assert.Equal(productId, row.ProductId);
        Assert.Equal(100m, row.SharePercent);
        await _repo.Received(1).GetLossesByProductAsync(_tenantId, null, null, From30, Today, default);
    }

    [Fact]
    public async Task GetLossesByProductAsync_store_and_reason_filters_are_forwarded_unchanged()
    {
        var storeId = Guid.NewGuid();
        var expected = new LossesByProductDto(TotalLoss: 0m, Products: []);

        _repo.GetLossesByProductAsync(_tenantId, storeId, "expired", From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetLossesByProductAsync(_tenantId, storeId, "expired", From30, Today);

        Assert.Empty(result.Products);
        await _repo.Received(1).GetLossesByProductAsync(_tenantId, storeId, "expired", From30, Today, default);
    }

    // No margin gate (ADR-027 §1): LossByProductRowDto carries no MarginAmount/MarginPercent
    // fields at all, and GetLossesByProductAsync's own signature has no includeMargin/
    // ClaimsPrincipal parameter -- unlike GetCategoryProductBreakdownAsync above, there is
    // nothing in this call path that could vary by caller role. Confirms both roles really do
    // differ on CanViewMargin (so this isn't vacuous), then shows the endpoint call itself never
    // consults it.
    [Fact]
    public async Task GetLossesByProductAsync_has_no_margin_gate_by_construction()
    {
        var storeManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.StoreManager));
        var networkManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.NetworkManager));
        Assert.False(storeManagerCanViewMargin);
        Assert.True(networkManagerCanViewMargin);

        var productId = Guid.NewGuid();
        var expected = new LossesByProductDto(
            TotalLoss: 300m,
            Products: new List<LossByProductRowDto> { new(productId, "Cheese", 5m, 300m, 100m) });

        _repo.GetLossesByProductAsync(_tenantId, null, null, From30, Today, default).Returns(expected);

        // Same call regardless of which role is asking -- the method takes no role/margin input.
        var result = await _sut.GetLossesByProductAsync(_tenantId, null, null, From30, Today);

        var row = Assert.Single(result.Products);
        Assert.Equal(300m, row.LossAmount);
        Assert.Equal(100m, row.SharePercent);
        await _repo.Received(1).GetLossesByProductAsync(_tenantId, null, null, From30, Today, default);
    }

    // ── TASK-489: GetLossesTrendAsync ────────────────────────────────────────

    [Fact]
    public async Task GetLossesTrendAsync_day_groupBy_delegates_to_repository()
    {
        var expected = new LossesTrendDto(
            Points: new List<LossesTrendPointDto>
            {
                new(Today, TotalLoss: 150m, Count: 3),
            },
            GroupBy: "day");

        _repo.GetLossesTrendAsync(_tenantId, null, From30, Today, "day", default)
             .Returns(expected);

        var result = await _sut.GetLossesTrendAsync(_tenantId, null, From30, Today, "day");

        Assert.Equal("day", result.GroupBy);
        var point = Assert.Single(result.Points);
        Assert.Equal(150m, point.TotalLoss);
        Assert.Equal(3, point.Count);
        await _repo.Received(1).GetLossesTrendAsync(_tenantId, null, From30, Today, "day", default);
    }

    [Fact]
    public async Task GetLossesTrendAsync_week_groupBy_passes_week_to_repository()
    {
        var expected = new LossesTrendDto(Points: [], GroupBy: "week");

        _repo.GetLossesTrendAsync(_tenantId, null, From30, Today, "week", default)
             .Returns(expected);

        var result = await _sut.GetLossesTrendAsync(_tenantId, null, From30, Today, "week");

        Assert.Equal("week", result.GroupBy);
        await _repo.Received(1).GetLossesTrendAsync(_tenantId, null, From30, Today, "week", default);
    }

    [Fact]
    public async Task GetLossesTrendAsync_store_filter_is_forwarded_unchanged()
    {
        var storeId = Guid.NewGuid();
        var expected = new LossesTrendDto(
            Points: new List<LossesTrendPointDto> { new(Today, TotalLoss: 40m, Count: 1) },
            GroupBy: "day");

        _repo.GetLossesTrendAsync(_tenantId, storeId, From30, Today, "day", default)
             .Returns(expected);

        var result = await _sut.GetLossesTrendAsync(_tenantId, storeId, From30, Today, "day");

        var point = Assert.Single(result.Points);
        Assert.Equal(40m, point.TotalLoss);
        await _repo.Received(1).GetLossesTrendAsync(_tenantId, storeId, From30, Today, "day", default);
    }

    // Empty-range / no-write-offs case: repository returns an empty points list rather than
    // null or throwing -- same "no data" shape as GetPosTopProductsAsync_empty_period above.
    [Fact]
    public async Task GetLossesTrendAsync_empty_range_returns_empty_points()
    {
        var expected = new LossesTrendDto(Points: [], GroupBy: "day");

        _repo.GetLossesTrendAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var result = await _sut.GetLossesTrendAsync(Guid.NewGuid(), null, Today, Today, "day");

        Assert.Empty(result.Points);
        Assert.Equal("day", result.GroupBy);
    }

    // ── TASK-482: GetProductSalesTrendAsync ──────────────────────────────────

    [Fact]
    public async Task GetProductSalesTrendAsync_day_groupBy_delegates_to_repository()
    {
        var productId = Guid.NewGuid();
        var expected = new ProductSalesTrendDto(
            ProductId: productId,
            ProductName: "Milk",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(Today, Revenue: 100m, Quantity: 10m, TransactionCount: 5, MarginAmount: null),
            },
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(expected);

        var result = await _sut.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", includeMargin: false);

        Assert.NotNull(result);
        Assert.Equal("day", result!.GroupBy);
        Assert.Equal(productId, result.ProductId);
        var point = Assert.Single(result.Points);
        Assert.Equal(100m, point.Revenue);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default);
    }

    [Fact]
    public async Task GetProductSalesTrendAsync_week_groupBy_passes_week_to_repository()
    {
        var productId = Guid.NewGuid();
        var expected = new ProductSalesTrendDto(
            ProductId: productId,
            ProductName: "Milk",
            Points: [],
            GroupBy: "week");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "week", false, default)
             .Returns(expected);

        var result = await _sut.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "week", includeMargin: false);

        Assert.NotNull(result);
        Assert.Equal("week", result!.GroupBy);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "week", false, default);
    }

    // Pins ADR-027's authorization contract end to end at this layer, same shape as
    // GetCategoryProductBreakdownAsync's margin test above (TASK-481): constructs the same
    // ClaimsPrincipal shape AnalyticsAuthorizationTests uses, resolves CanViewMargin exactly as
    // the controller will, and proves the resulting bool is what decides whether each point's
    // MarginAmount comes back null or populated.
    [Fact]
    public async Task GetProductSalesTrendAsync_margin_is_null_for_store_manager_and_populated_for_network_manager()
    {
        var productId = Guid.NewGuid();

        var withoutMargin = new ProductSalesTrendDto(
            ProductId: productId,
            ProductName: "Milk",
            Points: new List<ProductSalesTrendPointDto> { new(Today, 100m, 10m, 5, MarginAmount: null) },
            GroupBy: "day");

        var withMargin = new ProductSalesTrendDto(
            ProductId: productId,
            ProductName: "Milk",
            Points: new List<ProductSalesTrendPointDto> { new(Today, 100m, 10m, 5, MarginAmount: 15m) },
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(withoutMargin);
        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", true, default)
             .Returns(withMargin);

        var storeManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.StoreManager));
        var networkManagerCanViewMargin = AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.NetworkManager));
        Assert.False(storeManagerCanViewMargin);
        Assert.True(networkManagerCanViewMargin);

        var storeManagerResult = await _sut.GetProductSalesTrendAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: storeManagerCanViewMargin);
        var networkManagerResult = await _sut.GetProductSalesTrendAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: networkManagerCanViewMargin);

        Assert.All(storeManagerResult!.Points, p => Assert.Null(p.MarginAmount));
        Assert.All(networkManagerResult!.Points, p => Assert.NotNull(p.MarginAmount));

        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", true, default);
    }

    // Repository returns null when productId doesn't resolve to a real Item in the caller's
    // tenant scope -- that's the controller's NotFound() signal (GetProductSalesTrend:
    // "return result is null ? NotFound() : Ok(result);", mirroring ItemsController.GetById).
    // Proves the service is a pure pass-through of that null, same convention as
    // ItemServiceTests' GetByIdAsync-returns-null tests use for the equivalent case one layer
    // down. Controller action itself is a one-line ternary, consistent with this codebase having
    // no *ControllerTests.cs files anywhere -- controllers aren't unit-tested directly here.
    [Fact]
    public async Task GetProductSalesTrendAsync_returns_null_when_repository_finds_no_matching_product()
    {
        var unknownProductId = Guid.NewGuid();

        _repo.GetProductSalesTrendAsync(_tenantId, null, unknownProductId, From30, Today, "day", false, default)
             .Returns((ProductSalesTrendDto?)null);

        var result = await _sut.GetProductSalesTrendAsync(_tenantId, null, unknownProductId, From30, Today, "day", includeMargin: false);

        Assert.Null(result);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, unknownProductId, From30, Today, "day", false, default);
    }

    // ── TASK-590: GetProductSalesTrendComparisonAsync ────────────────────────

    // Calls the repository's GetProductSalesTrendAsync exactly twice -- once per date range --
    // and sums each window's points into the DTO's totals. Repository (not the service) owns the
    // actual points; this pins that the two ranges are forwarded distinctly and correctly summed.
    [Fact]
    public async Task GetProductSalesTrendComparisonAsync_calls_repository_twice_once_per_date_range()
    {
        var productId = Guid.NewGuid();
        var compareFrom = From30.AddDays(-31);
        var compareTo = From30.AddDays(-1);

        var current = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(Today, Revenue: 100m, Quantity: 10m, TransactionCount: 5, MarginAmount: null),
                new(Today.AddDays(-1), Revenue: 50m, Quantity: 5m, TransactionCount: 2, MarginAmount: null),
            },
            GroupBy: "day");

        var comparison = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(compareFrom, Revenue: 100m, Quantity: 10m, TransactionCount: 4, MarginAmount: null),
            },
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(current);
        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, compareFrom, compareTo, "day", false, default)
             .Returns(comparison);

        var result = await _sut.GetProductSalesTrendComparisonAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: false, compareFrom, compareTo);

        Assert.NotNull(result);
        Assert.Equal(productId, result!.ProductId);
        Assert.Equal("Paska", result.ProductName);
        Assert.Equal(2, result.Current.Count);
        Assert.Single(result.Comparison);
        Assert.Equal(150m, result.CurrentTotalRevenue);
        Assert.Equal(15m, result.CurrentTotalQuantity);
        Assert.Equal(100m, result.ComparisonTotalRevenue);
        Assert.Equal(10m, result.ComparisonTotalQuantity);

        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, productId, compareFrom, compareTo, "day", false, default);
    }

    // PercentChange is (current - previous) / previous * 100, rounded to 2 decimals (see
    // AnalyticsService.PercentChange) -- 150 vs 100 is a clean +50%.
    [Fact]
    public async Task GetProductSalesTrendComparisonAsync_percent_change_is_computed_via_percent_change_helper()
    {
        var productId = Guid.NewGuid();
        var compareFrom = From30.AddDays(-31);
        var compareTo = From30.AddDays(-1);

        var current = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(Today, Revenue: 150m, Quantity: 30m, TransactionCount: 5, MarginAmount: null),
            },
            GroupBy: "day");

        var comparison = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(compareFrom, Revenue: 100m, Quantity: 20m, TransactionCount: 4, MarginAmount: null),
            },
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(current);
        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, compareFrom, compareTo, "day", false, default)
             .Returns(comparison);

        var result = await _sut.GetProductSalesTrendComparisonAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: false, compareFrom, compareTo);

        Assert.Equal(50m, result!.RevenuePercentChange);
        Assert.Equal(50m, result.QuantityPercentChange);
    }

    // Baseline window with zero sales is a routine case (product only started selling
    // recently, or genuinely had none before the event), not an error -- repository returns a
    // valid DTO with an empty Points list (NOT null; null is reserved for "product not found",
    // see GetProductSalesTrendAsync_returns_null_when_repository_finds_no_matching_product
    // above). Comparison totals should come back zero and PercentChange null (PercentChange's
    // own "previous == 0 -> null" convention), without throwing.
    [Fact]
    public async Task GetProductSalesTrendComparisonAsync_zero_sales_baseline_produces_zero_totals_and_null_percent_change()
    {
        var productId = Guid.NewGuid();
        var compareFrom = From30.AddDays(-31);
        var compareTo = From30.AddDays(-1);

        var current = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(Today, Revenue: 100m, Quantity: 10m, TransactionCount: 5, MarginAmount: null),
            },
            GroupBy: "day");

        var comparison = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: [],
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(current);
        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, compareFrom, compareTo, "day", false, default)
             .Returns(comparison);

        var result = await _sut.GetProductSalesTrendComparisonAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: false, compareFrom, compareTo);

        Assert.NotNull(result);
        Assert.Empty(result!.Comparison);
        Assert.Equal(0m, result.ComparisonTotalRevenue);
        Assert.Equal(0m, result.ComparisonTotalQuantity);
        Assert.Null(result.RevenuePercentChange);
        Assert.Null(result.QuantityPercentChange);
    }

    // Repository returning null for the comparison-window call (defensive case -- current-window
    // productId/tenantId already resolved, so this can't newly occur through normal use, but the
    // service must not throw if it somehow did) is handled the same as the empty-Points case
    // above: zero totals, null percent-change, no exception.
    [Fact]
    public async Task GetProductSalesTrendComparisonAsync_null_comparison_window_does_not_throw()
    {
        var productId = Guid.NewGuid();
        var compareFrom = From30.AddDays(-31);
        var compareTo = From30.AddDays(-1);

        var current = new ProductSalesTrendDto(
            ProductId: productId, ProductName: "Paska",
            Points: new List<ProductSalesTrendPointDto>
            {
                new(Today, Revenue: 100m, Quantity: 10m, TransactionCount: 5, MarginAmount: null),
            },
            GroupBy: "day");

        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, From30, Today, "day", false, default)
             .Returns(current);
        _repo.GetProductSalesTrendAsync(_tenantId, null, productId, compareFrom, compareTo, "day", false, default)
             .Returns((ProductSalesTrendDto?)null);

        var result = await _sut.GetProductSalesTrendComparisonAsync(
            _tenantId, null, productId, From30, Today, "day", includeMargin: false, compareFrom, compareTo);

        Assert.NotNull(result);
        Assert.Empty(result!.Comparison);
        Assert.Equal(0m, result.ComparisonTotalRevenue);
        Assert.Null(result.RevenuePercentChange);
    }

    // Current-window call returning null (productId not found in tenant scope) short-circuits
    // the whole comparison -- mirrors GetProductSalesTrendAsync's own null/404 convention, and the
    // comparison-window repository call must never even fire (nothing meaningful to compare).
    [Fact]
    public async Task GetProductSalesTrendComparisonAsync_returns_null_when_current_window_product_not_found()
    {
        var unknownProductId = Guid.NewGuid();
        var compareFrom = From30.AddDays(-31);
        var compareTo = From30.AddDays(-1);

        _repo.GetProductSalesTrendAsync(_tenantId, null, unknownProductId, From30, Today, "day", false, default)
             .Returns((ProductSalesTrendDto?)null);

        var result = await _sut.GetProductSalesTrendComparisonAsync(
            _tenantId, null, unknownProductId, From30, Today, "day", includeMargin: false, compareFrom, compareTo);

        Assert.Null(result);
        await _repo.Received(1).GetProductSalesTrendAsync(_tenantId, null, unknownProductId, From30, Today, "day", false, default);
        await _repo.DidNotReceive().GetProductSalesTrendAsync(_tenantId, null, unknownProductId, compareFrom, compareTo, "day", false, default);
    }

    // ── TASK-490: GetWorstProductsAsync ──────────────────────────────────────

    // The whole point of this endpoint (see AnalyticsRepository.GetWorstProductsAsync's own
    // comments): a product with zero sales in the period must still appear, with SalesRevenue
    // coerced to 0 rather than the row silently vanishing the way it would from a plain GroupBy
    // over PosTransactionItems. The stock/sales merge itself lives in the repository (not
    // independently unit-tested anywhere in this codebase -- same precedent as every other
    // GetXxxAsync repository method in this file); this pins the DTO shape/pass-through at the
    // service layer.
    [Fact]
    public async Task GetWorstProductsAsync_zero_sales_product_has_zero_revenue()
    {
        var productId = Guid.NewGuid();
        var expected = new WorstProductsDto(Products: new List<WorstProductRowDto>
        {
            new(productId, "Dead Stock Item", SalesRevenue: 0m, UnitsSold: 0m, TransactionCount: 0, CurrentStock: 42m),
        });

        _repo.GetWorstProductsAsync(_tenantId, null, From30, Today, 10, default)
             .Returns(expected);

        var result = await _sut.GetWorstProductsAsync(_tenantId, null, From30, Today, 10);

        var row = Assert.Single(result.Products);
        Assert.Equal(0m, row.SalesRevenue);
        Assert.Equal(0, row.TransactionCount);
        Assert.Equal(42m, row.CurrentStock);
        await _repo.Received(1).GetWorstProductsAsync(_tenantId, null, From30, Today, 10, default);
    }

    [Fact]
    public async Task GetWorstProductsAsync_returns_items_ordered_ascending_by_revenue()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        var expected = new WorstProductsDto(Products: new List<WorstProductRowDto>
        {
            new(productA, "Stale Bread", SalesRevenue: 0m,  UnitsSold: 0m, TransactionCount: 0, CurrentStock: 20m),
            new(productB, "Slow Milk",   SalesRevenue: 15m, UnitsSold: 2m, TransactionCount: 1, CurrentStock: 8m),
        });

        _repo.GetWorstProductsAsync(_tenantId, null, From30, Today, 10, default)
             .Returns(expected);

        var result = await _sut.GetWorstProductsAsync(_tenantId, null, From30, Today, 10);

        Assert.Equal(2, result.Products.Count);
        Assert.True(result.Products[0].SalesRevenue <= result.Products[1].SalesRevenue,
            "Items should be ordered by revenue ascending (worst/zero first)");
        Assert.Equal(productA, result.Products[0].ProductId);
    }

    // The actual 1-100 clamp (mirrors pos/top-products' `if (limit is < 1 or > 100) limit = 10;`)
    // lives in AnalyticsController, not this service -- GetWorstProductsAsync forwards whatever
    // `limit` the controller already resolved, unchanged, same as every other limit-taking method
    // in this file (GetPosTopProductsAsync included). This codebase has no *ControllerTests.cs
    // anywhere (see GetProductSalesTrendAsync_returns_null_when_repository_finds_no_matching_product
    // above for the same precedent re: that action's 404 ternary), so the clamp math itself isn't
    // independently unit-tested; this pins the one thing actually testable at this layer -- that
    // whatever value arrives is passed through untouched, not silently re-clamped or defaulted a
    // second time.
    [Fact]
    public async Task GetWorstProductsAsync_limit_is_forwarded_unchanged_to_repository()
    {
        var expected = new WorstProductsDto(Products: []);

        _repo.GetWorstProductsAsync(_tenantId, null, From30, Today, 5, default)
             .Returns(expected);

        var result = await _sut.GetWorstProductsAsync(_tenantId, null, From30, Today, 5);

        Assert.Empty(result.Products);
        await _repo.Received(1).GetWorstProductsAsync(_tenantId, null, From30, Today, 5, default);
    }

    [Fact]
    public async Task GetWorstProductsAsync_store_filter_is_forwarded_and_current_stock_round_trips()
    {
        var storeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var expected = new WorstProductsDto(Products: new List<WorstProductRowDto>
        {
            new(productId, "Local Dead Stock", SalesRevenue: 0m, UnitsSold: 0m, TransactionCount: 0, CurrentStock: 5m),
        });

        _repo.GetWorstProductsAsync(_tenantId, storeId, From30, Today, 10, default)
             .Returns(expected);

        var result = await _sut.GetWorstProductsAsync(_tenantId, storeId, From30, Today, 10);

        var row = Assert.Single(result.Products);
        Assert.Equal(5m, row.CurrentStock);
        await _repo.Received(1).GetWorstProductsAsync(_tenantId, storeId, From30, Today, 10, default);
    }

    private static ClaimsPrincipal MakeUser(string role)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "TestAuth"));
}
