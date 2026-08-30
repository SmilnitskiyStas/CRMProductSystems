using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="TransferRepository.GetPagedAsync"/>'s new
/// <c>search</c>/<c>sortBy</c>/<c>sortDescending</c> params (TASK-630). See
/// <see cref="ItemRepositoryGetPagedBarcodeSearchIntegrationTests"/> for why this needs real
/// Postgres rather than InMemory.
/// </summary>
public sealed class TransferRepositoryGetPagedSearchSortIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _fromStoreId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _matchesByFromOnly;
    private Guid _matchesByToOnly;
    private Guid _matchesByTypeOnly;
    private Guid _matchesNeither;
    private Guid _categoryId;

    private string FromNeedle => $"FromNeedle-{_run}";
    private string ToNeedle => $"ToNeedle-{_run}";
    // TransferType is varchar(50) — keep well under that with a short slice of _run.
    private string TypeNeedle => $"custom_type_{_run[..8]}";

    public TransferRepositoryGetPagedSearchSortIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping TransferRepository.GetPagedAsync search/sort integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Transfer Search Sort Test {_run}", $"transfer-search-sort-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        // `otherStore`'s name embeds the raw `_run` GUID so every fixture row (each references
        // fromStore, toStore, and/or otherStore) matches `search: _run` — the shared dev DB
        // already has unrelated transfers, so an unscoped total-count assertion would be flaky.
        var fromStore = new Location { TenantId = _tenantId, Name = $"Store {FromNeedle} Extra" };
        var toStore = new Location { TenantId = _tenantId, Name = $"Store {ToNeedle} Extra" };
        var otherStore = new Location { TenantId = _tenantId, Name = $"Unrelated Store {_run}" };
        _fromStoreId = fromStore.Id;
        db.Locations.AddRange(fromStore, toStore, otherStore);

        var byFrom = new StockTransfer
        {
            TenantId = _tenantId, FromStoreId = fromStore.Id, ToStoreId = otherStore.Id,
            TransferType = "store_to_store", Status = "in_transit", CreatedAt = DateTime.UtcNow.AddMinutes(-40),
        };
        var byTo = new StockTransfer
        {
            TenantId = _tenantId, FromStoreId = otherStore.Id, ToStoreId = toStore.Id,
            TransferType = "store_to_store", Status = "received", CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        var byType = new StockTransfer
        {
            TenantId = _tenantId, FromStoreId = otherStore.Id, ToStoreId = otherStore.Id,
            TransferType = TypeNeedle, Status = "in_transit", CreatedAt = DateTime.UtcNow.AddMinutes(-20),
        };
        var neither = new StockTransfer
        {
            TenantId = _tenantId, FromStoreId = otherStore.Id, ToStoreId = otherStore.Id,
            TransferType = "cs_to_store", Status = "cancelled", CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        _matchesByFromOnly = byFrom.Id;
        _matchesByToOnly = byTo.Id;
        _matchesByTypeOnly = byType.Id;
        _matchesNeither = neither.Id;

        db.StockTransfers.AddRange(byFrom, byTo, byType, neither);

        // TASK-640: category_id/min_items/max_items fixtures. byFrom has 1 line item (of the
        // categorized item), byTo has 2, byType/neither have 0 — distinct counts for each bound.
        var category = new Category { TenantId = _tenantId, Name = $"Category {_run}" };
        _categoryId = category.Id;
        db.Categories.Add(category);

        var categorizedItem = new Item { TenantId = _tenantId, Name = $"Categorized Item {_run}", ManagementType = "MTS", CategoryId = category.Id };
        var plainItem = new Item { TenantId = _tenantId, Name = $"Plain Item {_run}", ManagementType = "MTS" };
        db.Items.AddRange(categorizedItem, plainItem);

        db.StockTransferItems.AddRange(
            new StockTransferItem { TransferId = byFrom.Id, ProductStockId = Guid.NewGuid(), ProductId = categorizedItem.Id, Quantity = 1, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) },
            new StockTransferItem { TransferId = byTo.Id, ProductStockId = Guid.NewGuid(), ProductId = plainItem.Id, Quantity = 1, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) },
            new StockTransferItem { TransferId = byTo.Id, ProductStockId = Guid.NewGuid(), ProductId = plainItem.Id, Quantity = 1, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        // stock_transfer_items cascade-deletes with their parent stock_transfers row (FK
        // OnDelete(Cascade) — see AppDbContext's StockTransferItem config).
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_transfers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM categories WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesFromStoreName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: FromNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByFromOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesToStoreName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: ToNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByToOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesTransferType()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: TypeNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByTypeOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortBy_FallsBackToDefaultWithoutThrowing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: "garbage-column", sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(4, total);
        Assert.Equal(_matchesNeither, items.First().Id); // most recently created (default desc)
    }

    [Fact]
    public async Task GetPagedAsync_SortByStatusAscending_OrdersCorrectly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeId: null, status: null, search: null, sortBy: "status", sortDescending: false,
            page: 1, pageSize: 30);

        var statuses = items.Select(t => t.Status).ToList();
        var sorted = statuses.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, statuses);
    }

    [Fact]
    public async Task GetPagedAsync_StoreIdAndStatusFilters_StillWorkUnchanged()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: _fromStoreId, status: "in_transit", search: null, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByFromOnly, items.Single().Id);
    }

    // TASK-640: category_id/min_items/max_items filters. byFrom has 1 line item (the categorized
    // item), byTo has 2, byType/neither have 0.
    [Fact]
    public async Task GetPagedAsync_CategoryIdFilter_ReturnsOnlyTransfersWithMatchingLineItem()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, categoryId: _categoryId);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByFromOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_MinItemsFilter_ExcludesTransfersWithFewerLineItems()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, minItems: 2);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByToOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_MaxItemsFilter_ExcludesTransfersWithMoreLineItems()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new TransferRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30, maxItems: 0);

        Assert.Equal(2, total);
        Assert.All(items, i => Assert.Contains(i.Id, new[] { _matchesByTypeOnly, _matchesNeither }));
    }

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
