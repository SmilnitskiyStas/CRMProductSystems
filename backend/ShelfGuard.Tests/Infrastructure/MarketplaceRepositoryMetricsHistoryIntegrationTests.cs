using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Marketplace;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-671: live-Postgres coverage for
/// <see cref="MarketplaceRepository.GetMetricsHistoryAsync"/> — the buyer-facing metric-history
/// read over <c>supplier_metrics_snapshots</c>. Needs a real Postgres for the <c>DateOnly</c> /
/// <c>date</c> window predicate and the <c>ORDER BY SnapshotDate</c>. Real <c>crm</c> superuser
/// connection (RLS bypassed — SQL correctness under test, not tenant isolation); a unique per-run
/// tenant so assertions ignore any pre-existing rows. Same connection/skip/cleanup pattern as
/// <see cref="MarketplaceRepositoryCoverageFilterIntegrationTests"/>.
/// </summary>
public sealed class MarketplaceRepositoryMetricsHistoryIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = TestPostgres.DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _supplierId;
    private Guid _otherSupplierId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    public MarketplaceRepositoryMetricsHistoryIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString = TestPostgres.ResolveConnectionString();

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
                $"Skipping marketplace metrics-history integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Metrics History Repo Test {_run}", $"metrics-history-repo-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var supplier = new Supplier { TenantId = _tenantId, Name = $"History {_run}" };
        var other    = new Supplier { TenantId = _tenantId, Name = $"Other {_run}" };
        _supplierId = supplier.Id;
        _otherSupplierId = other.Id;
        db.Suppliers.Add(supplier);
        db.Suppliers.Add(other);

        // 3 in-window rows seeded out of date order, 1 outside the 90-day window, 1 for another
        // supplier — the repo must return exactly the 3 in-window rows for _supplierId, ascending.
        db.SupplierMetricsSnapshots.AddRange(
            Snap(_supplierId, Today.AddDays(-10), rating: 4.5m, avg: 2.4m),
            Snap(_supplierId, Today.AddDays(-40), rating: 4.2m, avg: 3.1m),
            Snap(_supplierId, Today.AddDays(-1),  rating: 4.8m, avg: 2.0m),
            Snap(_supplierId, Today.AddDays(-200), rating: 3.0m, avg: 5.0m),   // outside a 90-day window
            Snap(_otherSupplierId, Today.AddDays(-5), rating: 1.0m, avg: 9.0m)); // different supplier

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_metrics_snapshots WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM suppliers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_WindowFiltersAndOrdersAscending()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var rows = await repo.GetMetricsHistoryAsync(_supplierId, 90);

        Assert.Equal(3, rows.Count);
        Assert.Equal(
            new[] { Today.AddDays(-40), Today.AddDays(-10), Today.AddDays(-1) },
            rows.Select(r => r.SnapshotDate).ToArray());
        Assert.All(rows, r => Assert.Equal(_supplierId, r.SupplierId));
        Assert.Equal(4.2m, rows[0].Rating);          // oldest in-window row first
        Assert.Equal(2.0m, rows[^1].AvgDeliveryDays); // newest last
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_NarrowWindow_ExcludesOlderRows()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var rows = await repo.GetMetricsHistoryAsync(_supplierId, 15);

        Assert.Equal(
            new[] { Today.AddDays(-10), Today.AddDays(-1) },
            rows.Select(r => r.SnapshotDate).ToArray());
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_UnknownSupplier_ReturnsEmpty()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        Assert.Empty(await repo.GetMetricsHistoryAsync(Guid.NewGuid(), 90));
    }

    private SupplierMetricsSnapshot Snap(Guid supplierId, DateOnly date, decimal rating, decimal avg) =>
        new()
        {
            SupplierId         = supplierId,
            TenantId           = _tenantId,
            SnapshotDate       = date,
            Rating             = rating,
            AvgDeliveryDays    = avg,
            OrderAccuracy      = 0.95m,
            CancellationRate   = 0.02m,
            ResponseTimeHours  = 6.0m,
            DeliverySampleSize = 8,
            ResponseSampleSize = 3,
        };

    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
