using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="StockRepository.GetPagedAsync"/>'s new
/// <c>search</c>/<c>sortBy</c>/<c>sortDescending</c> params (TASK-630) — including the barcode
/// branch, which shares <see cref="ItemRepository.GetPagedAsync"/>'s (TASK-601)
/// <c>EF.Functions.JsonContains</c> convention for the jsonb-mapped <c>Item.Barcodes</c> column.
/// Needs real Postgres, not InMemory — see
/// <see cref="ItemRepositoryGetPagedBarcodeSearchIntegrationTests"/> for the documented
/// InMemory-passes-but-Postgres-fails risk this guards against.
/// </summary>
public sealed class StockRepositoryGetPagedSearchSortIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;
    private DbContextOptions<AppDbContext>? _options;

    private Guid _tenantId;
    private Guid _storeId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _matchesByNameOnly;
    private Guid _matchesByBarcodeOnly;
    private Guid _matchesByBatchOnly;
    private Guid _matchesNeither;
    private Guid _nearestExpiry;
    private Guid _farthestExpiry;

    private string NameNeedle => $"NameNeedle-{_run}";
    private string Barcode => $"BC-{_run}-needle";
    private string BatchNeedle => $"BATCH-{_run}-needle";

    public StockRepositoryGetPagedSearchSortIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping StockRepository.GetPagedAsync search/sort integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Stock Search Sort Test {_run}", $"stock-search-sort-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var store = new Location { TenantId = _tenantId, Name = "Test Store" };
        var otherStore = new Location { TenantId = _tenantId, Name = "Other Store" };
        _storeId = store.Id;
        db.Locations.AddRange(store, otherStore);

        // Every product name embeds the raw `_run` GUID (even "unrelated" ones) so
        // `search: _run` reliably scopes a query to just this test's 4 rows — the shared dev DB
        // already has hundreds of unrelated stock rows, so an unscoped total/order assertion
        // would be flaky against real data.
        var productByName = new Item { TenantId = _tenantId, Name = $"Product {NameNeedle} Extra", Barcodes = [$"BC-{_run}-other-a"], ManagementType = "MTS" };
        var productByBarcode = new Item { TenantId = _tenantId, Name = $"Unrelated Name A {_run}", Barcodes = [Barcode], ManagementType = "MTS" };
        var productNeutral = new Item { TenantId = _tenantId, Name = $"Totally Unrelated Product {_run}", Barcodes = [$"BC-{_run}-other-b"], ManagementType = "MTS" };
        db.Items.AddRange(productByName, productByBarcode, productNeutral);

        var byName = new ProductStock
        {
            TenantId = _tenantId, ProductId = productByName.Id, StoreId = store.Id,
            Quantity = 5, QuantityInitial = 5, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "safe", BatchNumber = "REGULAR-1", LastCheckedAt = DateTime.UtcNow,
        };
        var byBarcode = new ProductStock
        {
            TenantId = _tenantId, ProductId = productByBarcode.Id, StoreId = otherStore.Id,
            Quantity = 3, QuantityInitial = 3, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            Status = "critical", BatchNumber = "REGULAR-2", LastCheckedAt = DateTime.UtcNow,
        };
        var byBatch = new ProductStock
        {
            TenantId = _tenantId, ProductId = productNeutral.Id, StoreId = otherStore.Id,
            Quantity = 8, QuantityInitial = 8, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
            Status = "safe", BatchNumber = BatchNeedle, LastCheckedAt = DateTime.UtcNow,
        };
        var neither = new ProductStock
        {
            TenantId = _tenantId, ProductId = productNeutral.Id, StoreId = otherStore.Id,
            Quantity = 1, QuantityInitial = 1, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Status = "warning", BatchNumber = "REGULAR-3", LastCheckedAt = DateTime.UtcNow,
        };
        _matchesByNameOnly = byName.Id;
        _matchesByBarcodeOnly = byBarcode.Id;
        _matchesByBatchOnly = byBatch.Id;
        _matchesNeither = neither.Id;
        _nearestExpiry = byBarcode.Id;   // +2 days
        _farthestExpiry = byBatch.Id;    // +60 days

        db.ProductStocks.AddRange(byName, byBarcode, byBatch, neither);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesProductName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: NameNeedle, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByNameOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesExactBarcode_EvenWhenNameDoesNotMatch()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: Barcode, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByBarcodeOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesBatchNumber()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: BatchNeedle, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByBatchOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesNothing_ReturnsEmpty()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: $"{_run}-nonexistent-anything", sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetPagedAsync_DefaultSortByExpiryDate_OmittedSortDescending_IsAscendingNearestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        // No sortBy, no sortDescending — must preserve the pre-existing FEFO
        // nearest-expiry-first order (ascending), not flip to descending. Scoped via
        // `search: _run` so the total/order isn't polluted by the shared dev DB's other
        // (hundreds of) unrelated stock rows.
        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: _run, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(4, total);
        Assert.Equal(_nearestExpiry, items.First().Id);
        Assert.Equal(_farthestExpiry, items.Last().Id);
    }

    [Fact]
    public async Task GetPagedAsync_ExplicitSortDescendingTrue_OnDefaultKey_FlipsToFarthestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: _run, sortBy: "expirydate", sortDescending: true, page: 1, pageSize: 30);

        Assert.Equal(_farthestExpiry, items.First().Id);
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortBy_FallsBackToDefaultWithoutThrowing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: _run, sortBy: "garbage-column", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(4, total);
        Assert.Equal(_nearestExpiry, items.First().Id); // falls back to default (expirydate asc)
    }

    [Fact]
    public async Task GetPagedAsync_SortByQuantityDescending_OrdersHighestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeIds: null, status: null, zoneId: null, productId: null,
            search: _run, sortBy: "quantity", sortDescending: true, page: 1, pageSize: 30);

        Assert.Equal(_matchesByBatchOnly, items.First().Id); // quantity 8, the highest
    }

    [Fact]
    public async Task GetPagedAsync_StoreIdsAndStatusFilters_StillWorkUnchanged()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new StockRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeIds: [_storeId], status: "safe", zoneId: null, productId: null,
            search: null, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByNameOnly, items.Single().Id);
    }

    private AppDbContext NewContext()
    {
        _options ??= new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build())
            .IgnoreManyServiceProvidersWarning()
            .Options;
        return new AppDbContext(_options);
    }
}
