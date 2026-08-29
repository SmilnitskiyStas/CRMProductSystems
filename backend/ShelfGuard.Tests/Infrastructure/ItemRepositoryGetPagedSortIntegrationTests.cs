using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="ItemRepository.GetPagedAsync"/>'s new
/// <c>sortBy</c>/<c>sortDescending</c> params (TASK-632) — including the "category" key's
/// null-last-regardless-of-direction requirement and the "barcode" key's documented fallback to
/// name order (see <see cref="ItemRepository"/>'s <c>ApplySort</c> comment). Needs real Postgres,
/// not InMemory — same reasoning as
/// <see cref="ItemRepositoryGetPagedBarcodeSearchIntegrationTests"/>.
///
/// Every row's Name embeds the raw <c>_run</c> GUID and every call passes <c>search: _run</c> —
/// the shared dev DB already has a real seeded catalog, so an unscoped total/order assertion
/// would be flaky (or simply wrong) against that pre-existing data.
/// </summary>
public sealed class ItemRepositoryGetPagedSortIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _categoryAlphaId;
    private Guid _categoryZebraId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    // Names deliberately alphabetically ordered AAA < MMM < ZZZ (the `_run` suffix is shared by
    // all three, so it never affects the comparison — the differing prefix does).
    private Guid _itemAlpha;   // CategoryId = Alpha, cheapest purchase price, lowest stock bounds
    private Guid _itemNoCat;   // CategoryId = null, highest retail price
    private Guid _itemZebra;   // CategoryId = Zebra, highest max stock

    public ItemRepositoryGetPagedSortIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping ItemRepository.GetPagedAsync sort integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"GetPaged Sort Test {_run}", $"get-paged-sort-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var categoryAlpha = new Category { TenantId = _tenantId, Name = $"Alpha Category {_run}" };
        var categoryZebra = new Category { TenantId = _tenantId, Name = $"Zebra Category {_run}" };
        _categoryAlphaId = categoryAlpha.Id;
        _categoryZebraId = categoryZebra.Id;
        db.Categories.AddRange(categoryAlpha, categoryZebra);

        var itemAlpha = new Item
        {
            TenantId = _tenantId, Name = $"AAA Item {_run}", ManagementType = "MTS",
            CategoryId = categoryAlpha.Id,
            PricePurchase = 100m, PriceRetail = 10m, MinStock = 5m, MaxStock = 50m,
        };
        var itemNoCat = new Item
        {
            TenantId = _tenantId, Name = $"MMM Item {_run}", ManagementType = "MTS",
            CategoryId = null,
            PricePurchase = 5m, PriceRetail = 200m, MinStock = 10m, MaxStock = 10m,
        };
        var itemZebra = new Item
        {
            TenantId = _tenantId, Name = $"ZZZ Item {_run}", ManagementType = "MTS",
            CategoryId = categoryZebra.Id,
            PricePurchase = 20m, PriceRetail = 50m, MinStock = 1m, MaxStock = 100m,
        };
        _itemAlpha = itemAlpha.Id;
        _itemNoCat = itemNoCat.Id;
        _itemZebra = itemZebra.Id;

        db.Items.AddRange(itemAlpha, itemNoCat, itemZebra);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM categories WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_DefaultSortOmitted_IsAscendingByName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(3, total);
        Assert.Equal(_itemAlpha, items.First().Id); // "AAA..."
        Assert.Equal(_itemZebra, items.Last().Id);   // "ZZZ..."
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortBy_FallsBackToDefaultWithoutThrowing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "garbage-column", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(3, total);
        Assert.Equal(_itemAlpha, items.First().Id); // falls back to default (name asc)
    }

    [Fact]
    public async Task GetPagedAsync_SortByCategoryAscending_NullCategorySortsLast()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "category", sortDescending: false, page: 1, pageSize: 30);

        Assert.Equal(_itemAlpha, items[0].Id);  // "Alpha Category" — lowest
        Assert.Equal(_itemZebra, items[1].Id);  // "Zebra Category"
        Assert.Equal(_itemNoCat, items[2].Id);  // no category — last regardless of direction
    }

    [Fact]
    public async Task GetPagedAsync_SortByCategoryDescending_NullCategoryStillSortsLast()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "category", sortDescending: true, page: 1, pageSize: 30);

        Assert.Equal(_itemZebra, items[0].Id);  // "Zebra Category" — highest, now first
        Assert.Equal(_itemAlpha, items[1].Id);  // "Alpha Category"
        Assert.Equal(_itemNoCat, items[2].Id);  // no category — still last
    }

    [Fact]
    public async Task GetPagedAsync_SortByPurchasePrice_OmittedSortDescending_DefaultsToDescending()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "purchaseprice", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(_itemAlpha, items.First().Id); // 100 — highest purchase price
    }

    [Fact]
    public async Task GetPagedAsync_SortByPurchasePriceAscending_LowestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "purchaseprice", sortDescending: false, page: 1, pageSize: 30);

        Assert.Equal(_itemNoCat, items.First().Id); // 5 — lowest purchase price
    }

    [Fact]
    public async Task GetPagedAsync_SortByRetailPrice_OmittedSortDescending_DefaultsToDescending()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "retailprice", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(_itemNoCat, items.First().Id); // 200 — highest retail price
    }

    [Fact]
    public async Task GetPagedAsync_SortByMinStock_OmittedSortDescending_DefaultsToDescending()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "minstock", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(_itemNoCat, items.First().Id); // 10 — highest MinStock
    }

    [Fact]
    public async Task GetPagedAsync_SortByMaxStock_OmittedSortDescending_DefaultsToDescending()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "maxstock", sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(_itemZebra, items.First().Id); // 100 — highest MaxStock
    }

    [Fact]
    public async Task GetPagedAsync_SortByBarcode_FallsBackToNameOrder()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        // Ascending explicit: same as sortBy=name ascending (documented fallback, TASK-632).
        var (ascItems, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "barcode", sortDescending: false, page: 1, pageSize: 30);
        Assert.Equal(_itemAlpha, ascItems.First().Id);   // "AAA..."
        Assert.Equal(_itemZebra, ascItems.Last().Id);    // "ZZZ..."

        // Omitted sortDescending on a non-default key -> descending, so also same as
        // sortBy=name descending.
        var (descItems, _) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "barcode", sortDescending: null, page: 1, pageSize: 30);
        Assert.Equal(_itemZebra, descItems.First().Id);  // "ZZZ..."
        Assert.Equal(_itemAlpha, descItems.Last().Id);   // "AAA..."
    }

    [Fact]
    public async Task GetPagedAsync_CategoryIdFilter_StillWorksUnchangedWithSort()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: _categoryAlphaId, segmentId: null, managementType: null, search: _run, ids: null,
            sortBy: "retailprice", sortDescending: true, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_itemAlpha, items.Single().Id);
    }

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
