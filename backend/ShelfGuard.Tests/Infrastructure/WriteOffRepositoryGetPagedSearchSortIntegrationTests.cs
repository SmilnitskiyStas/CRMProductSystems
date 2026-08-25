using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="WriteOffRepository.GetPagedAsync"/>'s new
/// <c>search</c>/<c>sortBy</c>/<c>sortDescending</c> params (TASK-630). See
/// <see cref="ItemRepositoryGetPagedBarcodeSearchIntegrationTests"/> for why this needs real
/// Postgres rather than InMemory.
/// </summary>
public sealed class WriteOffRepositoryGetPagedSearchSortIntegrationTests : IAsyncLifetime
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

    private Guid _matchesByStoreOnly;
    private Guid _matchesByReasonOnly;
    private Guid _matchesNeither;
    private Guid _highestNetLoss;
    private Guid _lowestNetLoss;

    private string StoreNeedle => $"StoreNeedle-{_run}";
    // Reason is varchar(50) — keep well under that (with the "other: " prefix) using a short slice of _run.
    private string ReasonNeedle => $"reason-needle-{_run[..8]}";

    public WriteOffRepositoryGetPagedSearchSortIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping WriteOffRepository.GetPagedAsync search/sort integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"WriteOff Search Sort Test {_run}", $"writeoff-search-sort-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        // `otherStore`'s name embeds the raw `_run` GUID so every fixture row (each references
        // matchStore and/or otherStore) matches `search: _run` — the shared dev DB already has
        // unrelated write-offs, so an unscoped total-count/ordering assertion would be flaky.
        var matchStore = new Location { TenantId = _tenantId, Name = $"Store {StoreNeedle} Extra" };
        var otherStore = new Location { TenantId = _tenantId, Name = $"Unrelated Store {_run}" };
        _storeId = matchStore.Id;
        db.Locations.AddRange(matchStore, otherStore);

        var byStore = new WriteOff
        {
            TenantId = _tenantId, StoreId = matchStore.Id, Status = "pending_approval", Reason = "expired",
            TotalLossAmountPurchase = 10m, TotalReimbursementAmount = 0m, CreatedAt = DateTime.UtcNow.AddMinutes(-40),
        };
        var byReason = new WriteOff
        {
            TenantId = _tenantId, StoreId = otherStore.Id, Status = "approved", Reason = $"other: {ReasonNeedle}",
            TotalLossAmountPurchase = 100m, TotalReimbursementAmount = 0m, CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        var neither = new WriteOff
        {
            TenantId = _tenantId, StoreId = otherStore.Id, Status = "rejected", Reason = "damaged",
            TotalLossAmountPurchase = 50m, TotalReimbursementAmount = 20m, CreatedAt = DateTime.UtcNow.AddMinutes(-20),
        };
        _matchesByStoreOnly = byStore.Id;
        _matchesByReasonOnly = byReason.Id;
        _matchesNeither = neither.Id;
        _highestNetLoss = byReason.Id; // 100 - 0 = 100
        _lowestNetLoss = byStore.Id;   // 10 - 0 = 10

        db.WriteOffs.AddRange(byStore, byReason, neither);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM write_offs WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesStoreName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: StoreNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByStoreOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesReason()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: ReasonNeedle, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByReasonOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortBy_FallsBackToDefaultWithoutThrowing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        // Scoped via `search: _run` so the total/order isn't polluted by unrelated write-offs
        // already sitting in the shared dev DB.
        var (items, total) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: "garbage-column", sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(3, total);
        Assert.Equal(_matchesNeither, items.First().Id); // most recently created (default desc)
    }

    [Fact]
    public async Task GetPagedAsync_SortByNetLossDescending_OrdersHighestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: "netloss", sortDescending: true,
            page: 1, pageSize: 30);

        Assert.Equal(_highestNetLoss, items.First().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SortByNetLossAscending_OrdersLowestFirst()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        var (items, _) = await repo.GetPagedAsync(
            storeId: null, status: null, search: _run, sortBy: "netloss", sortDescending: false,
            page: 1, pageSize: 30);

        Assert.Equal(_lowestNetLoss, items.First().Id);
    }

    [Fact]
    public async Task GetPagedAsync_StoreIdAndStatusFilters_StillWorkUnchanged()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new WriteOffRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            storeId: _storeId, status: "pending_approval", search: null, sortBy: null, sortDescending: null,
            page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByStoreOnly, items.Single().Id);
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
