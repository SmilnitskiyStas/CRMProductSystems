using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-596: live-Postgres coverage for <see cref="ItemRepository.GetByAnyBarcodeAsync"/>.
/// Needs real Postgres, not InMemory — the method's whole point is
/// <c>EF.Functions.JsonExistAny</c>, which only the Npgsql provider translates (to the `?|`
/// jsonb-array-overlap operator); InMemory would either no-op or throw. Same
/// connection/skip/cleanup pattern as <see cref="PriceSegmentsRepositoryIntegrationTests"/>: real
/// `crm` superuser connection (bypasses RLS — SQL correctness under test, not tenant isolation),
/// unique per-run barcode values so no collision with any pre-existing row is possible.
/// </summary>
public sealed class ItemRepositoryGetByAnyBarcodeIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _matchesFirstNeedle;
    private Guid _matchesSecondNeedle;
    private Guid _matchesBothNeedles;
    private Guid _matchesNeither;

    private string Needle1 => $"BC-{_run}-1";
    private string Needle2 => $"BC-{_run}-2";
    private string Unrelated => $"BC-{_run}-unrelated";

    public ItemRepositoryGetByAnyBarcodeIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping ItemRepository.GetByAnyBarcodeAsync integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"GetByAnyBarcode Repo Test {_run}", $"get-by-any-barcode-repo-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var matchesFirst = new Item { TenantId = _tenantId, Name = "Matches Needle 1", Barcodes = [Needle1, Unrelated] };
        var matchesSecond = new Item { TenantId = _tenantId, Name = "Matches Needle 2", Barcodes = [Needle2] };
        var matchesBoth = new Item { TenantId = _tenantId, Name = "Matches Both Needles", Barcodes = [Needle1, Needle2] };
        var matchesNeither = new Item { TenantId = _tenantId, Name = "Matches Neither", Barcodes = [Unrelated] };
        _matchesFirstNeedle = matchesFirst.Id;
        _matchesSecondNeedle = matchesSecond.Id;
        _matchesBothNeedles = matchesBoth.Id;
        _matchesNeither = matchesNeither.Id;

        db.Items.AddRange(matchesFirst, matchesSecond, matchesBoth, matchesNeither);
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
    public async Task GetByAnyBarcodeAsync_ReturnsItemsMatchingAnyGivenBarcode_DedupedAndExcludingNonMatches()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var found = await repo.GetByAnyBarcodeAsync([Needle1, Needle2, $"BC-{_run}-nonexistent"]);

        var foundIds = found.Select(i => i.Id).ToList();
        Assert.Equal(3, found.Count); // matchesFirst, matchesSecond, matchesBoth — deduped, not 4× or 6×
        Assert.Contains(_matchesFirstNeedle, foundIds);
        Assert.Contains(_matchesSecondNeedle, foundIds);
        Assert.Contains(_matchesBothNeedles, foundIds);
        Assert.DoesNotContain(_matchesNeither, foundIds);
        Assert.Equal(foundIds.Count, foundIds.Distinct().Count()); // no duplicate rows for matchesBoth
    }

    [Fact]
    public async Task GetByAnyBarcodeAsync_NoBarcodesMatch_ReturnsEmpty()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var found = await repo.GetByAnyBarcodeAsync([$"BC-{_run}-totally-unrelated-to-anything"]);

        Assert.Empty(found);
    }

    [Fact]
    public async Task GetByAnyBarcodeAsync_EmptyInput_ReturnsEmptyWithoutQuerying()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new ItemRepository(db);

        var found = await repo.GetByAnyBarcodeAsync([]);

        Assert.Empty(found);
    }

    // KI-035: one shared, process-wide pooled data source instead of a per-test-instance
    // NpgsqlDataSource that was never disposed. See TestPostgres.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
