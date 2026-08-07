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
                    SalesRevenue: 500m, UnitsSold: 50m, MarginAmount: null, MarginPercent: null),
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
                new(productId, "Milk", 5, 1, 0, 0, 60m, 500m, 50m, MarginAmount: null, MarginPercent: null),
            });

        var withMargin = new CategoryProductBreakdownDto(
            CategoryId: categoryId,
            CategoryName: "Dairy",
            Products: new List<CategoryProductRowDto>
            {
                new(productId, "Milk", 5, 1, 0, 0, 60m, 500m, 50m, MarginAmount: 150m, MarginPercent: 30m),
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

    private static ClaimsPrincipal MakeUser(string role)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "TestAuth"));
}
