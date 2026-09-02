using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="ReceiptRepository.GetPagedAsync"/>'s new
/// <c>search</c>/<c>sortBy</c>/<c>sortDescending</c> params (TASK-630). Needs real Postgres, not
/// InMemory — the <c>EF.Functions.ILike</c> OR-across-navigation predicate has the same documented
/// InMemory-passes-but-Postgres-fails risk flagged on <see cref="ItemRepository.GetByBarcodeAsync"/>
/// and exercised by <see cref="ItemRepositoryGetPagedBarcodeSearchIntegrationTests"/>.
/// </summary>
public sealed class ReceiptRepositoryGetPagedSearchSortIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _storeId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _matchesBySupplierOnly;
    private Guid _matchesByDestinationOnly;
    private Guid _matchesNeither;
    private Guid _categoryId;

    private string SupplierNeedle => $"SupplierNeedle-{_run}";
    private string DestinationNeedle => $"DestNeedle-{_run}";

    public ReceiptRepositoryGetPagedSearchSortIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping ReceiptRepository.GetPagedAsync search/sort integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Receipt Search Sort Test {_run}", $"receipt-search-sort-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        // Every fixture row's supplier/destination name embeds the raw `_run` GUID (even the
        // "neither" row's), so `search: _run` reliably scopes a query to just this test's 3
        // rows — the shared dev DB already has unrelated receipts, so an unscoped total-count
        // assertion would be flaky against real data.
        var destStore = new Location { TenantId = _tenantId, Name = $"Store {DestinationNeedle} Extra" };
        var otherStore = new Location { TenantId = _tenantId, Name = $"Unrelated Destination Store {_run}" };
        _storeId = destStore.Id;
        db.Locations.AddRange(destStore, otherStore);

        var supplierMatch = new Supplier { TenantId = _tenantId, Name = $"Supplier {SupplierNeedle} Co" };
        var supplierNoMatch = new Supplier { TenantId = _tenantId, Name = $"Totally Unrelated Supplier {_run}" };
        db.Suppliers.AddRange(supplierMatch, supplierNoMatch);

        var bySupplier = new StockReceipt
        {
            TenantId = _tenantId, SupplierId = supplierMatch.Id, DestinationStoreId = otherStore.Id,
            Status = "draft", CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        var byDestination = new StockReceipt
        {
            TenantId = _tenantId, SupplierId = supplierNoMatch.Id, DestinationStoreId = destStore.Id,
            Status = "received", CreatedAt = DateTime.UtcNow.AddMinutes(-20),
        };
        var neither = new StockReceipt
        {
            TenantId = _tenantId, SupplierId = supplierNoMatch.Id, DestinationStoreId = otherStore.Id,
            Status = "draft", CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        _matchesBySupplierOnly = bySupplier.Id;
        _matchesByDestinationOnly = byDestination.Id;
        _matchesNeither = neither.Id;

        db.StockReceipts.AddRange(bySupplier, byDestination, neither);

        // TASK-640: category_id/min_items/max_items fixtures. bySupplier has 1 line item (of the
        // categorized item), byDestination has 2, neither has 0 — distinct counts for each bound.
        var category = new PlatformCategory { Name = $"Category {_run}" };
        _categoryId = category.Id;
        db.PlatformCategories.Add(category);

        var categorizedItem = new Item { TenantId = _tenantId, Name = $"Categorized Item {_run}", ManagementType = "MTS", CategoryId = category.Id };
        var plainItem = new Item { TenantId = _tenantId, Name = $"Plain Item {_run}", ManagementType = "MTS" };
        db.Items.AddRange(categorizedItem, plainItem);

        db.StockReceiptItems.AddRange(
            new StockReceiptItem { ReceiptId = bySupplier.Id, ProductId = categorizedItem.Id, QuantityOrdered = 1 },
            new StockReceiptItem { ReceiptId = byDestination.Id, ProductId = plainItem.Id, QuantityOrdered = 1 },
            new StockReceiptItem { ReceiptId = byDestination.Id, ProductId = plainItem.Id, QuantityOrdered = 1 });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        // stock_receipt_items cascade-deletes with their parent stock_receipts row (FK
        // OnDelete(Cascade) — see AppDbContext's StockReceiptItem config).
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_receipts WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform_categories WHERE \"Name\" LIKE {"%" + _run + "%"}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM suppliers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesSupplierName_EvenWhenDestinationDoesNotMatch()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: SupplierNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesBySupplierOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesDestinationStoreName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: DestinationNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByDestinationOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesNeither_ReturnsEmpty()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: $"{_run}-nonexistent-anything", sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortBy_FallsBackToDefaultWithoutThrowing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        // Should not throw and should behave like the default (createdat desc — newest first).
        // Scoped via `search: _run` (embedded in every fixture row's supplier/destination name)
        // so the total isn't polluted by unrelated receipts already sitting in the shared dev DB.
        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: "not-a-real-column", sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(3, total);
        Assert.Equal(_matchesNeither, items.First().Id); // most recently created
    }

    [Fact]
    public async Task GetPagedAsync_SortByStatusAscending_OrdersCorrectly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeId: null, status: null, search: null, sortBy: "status", sortDescending: false,
            page: 1, pageSize: 30);

        var statuses = items.Select(r => r.Status).ToList();
        var sorted = statuses.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, statuses);
    }

    [Fact]
    public async Task GetPagedAsync_StoreIdAndStatusFilters_StillWorkUnchanged()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: _storeId, status: "received", search: null, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByDestinationOnly, items.Single().Id);
    }

    // TASK-640: category_id/min_items/max_items filters. bySupplier has 1 line item (the
    // categorized item), byDestination has 2, neither has 0.
    [Fact]
    public async Task GetPagedAsync_CategoryIdFilter_ReturnsOnlyReceiptsWithMatchingLineItem()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, categoryId: _categoryId);

        Assert.Equal(1, total);
        Assert.Equal(_matchesBySupplierOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_MinItemsFilter_ExcludesReceiptsWithFewerLineItems()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, minItems: 2);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByDestinationOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_MaxItemsFilter_ExcludesReceiptsWithMoreLineItems()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ReceiptRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, maxItems: 0);

        Assert.Equal(1, total);
        Assert.Equal(_matchesNeither, items.Single().Id);
    }

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
