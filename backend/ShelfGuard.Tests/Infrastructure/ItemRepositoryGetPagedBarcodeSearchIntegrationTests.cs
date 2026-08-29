using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres coverage for <see cref="ItemRepository.GetPagedAsync"/>'s <c>search</c> filter
/// now also matching an exact barcode via <c>EF.Functions.JsonContains</c>, combined with the
/// pre-existing name <c>ILike</c> via an OR inside the same predicate. Needs real Postgres, not
/// InMemory — same reasoning as <see cref="ItemRepositoryGetByAnyBarcodeIntegrationTests"/> and
/// the class-level doc comment on <see cref="ShelfGuard.Tests.Catalog.ItemRepositoryGetPagedTests"/>,
/// which explicitly flags this exact gap (ILike/JsonContains translation only provably works
/// against a real Npgsql provider — this repo has a documented history of LINQ shapes that build
/// and pass against InMemory but throw against real Postgres, see
/// <see cref="ItemRepository.GetByBarcodeAsync"/>'s own comment).
/// </summary>
public sealed class ItemRepositoryGetPagedBarcodeSearchIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;
    private DbContextOptions<AppDbContext>? _options;

    private Guid _tenantId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _matchesByBarcodeOnly;
    private Guid _matchesByNameOnly;
    private Guid _matchesNeither;

    private string Barcode => $"BC-{_run}-needle";
    private string NameNeedle => $"NameNeedle-{_run}";

    public ItemRepositoryGetPagedBarcodeSearchIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping ItemRepository.GetPagedAsync barcode-search integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"GetPaged Barcode Search Test {_run}", $"get-paged-barcode-search-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var byBarcode = new Item { TenantId = _tenantId, Name = "Unrelated Name A", Barcodes = [Barcode], ManagementType = "MTS" };
        var byName = new Item { TenantId = _tenantId, Name = $"Product {NameNeedle} Extra", Barcodes = [$"BC-{_run}-other"], ManagementType = "MTS" };
        var neither = new Item { TenantId = _tenantId, Name = "Totally Unrelated Product", Barcodes = [$"BC-{_run}-unrelated"], ManagementType = "MTS" };
        _matchesByBarcodeOnly = byBarcode.Id;
        _matchesByNameOnly = byName.Id;
        _matchesNeither = neither.Id;

        db.Items.AddRange(byBarcode, byName, neither);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesExactBarcode_EvenWhenNameDoesNotMatch()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: Barcode,
            ids: null, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByBarcodeOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchStillMatchesNameSubstring_RegressionCheck()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: NameNeedle,
            ids: null, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(_matchesByNameOnly, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchMatchesNeitherNameNorBarcode_ReturnsEmpty()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: $"BC-{_run}-nonexistent-anything",
            ids: null, sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(0, total);
        Assert.Empty(items);
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
