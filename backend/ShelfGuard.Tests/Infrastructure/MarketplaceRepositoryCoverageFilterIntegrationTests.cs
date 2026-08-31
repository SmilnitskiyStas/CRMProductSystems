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
/// TASK-651: live-Postgres coverage for <see cref="MarketplaceRepository"/>'s delivery-coverage
/// region filter (<c>BuildPublicQuery</c> / <c>SearchSuppliersAsync</c>). Needs real Postgres,
/// not InMemory — the whole point is the server-side <c>jsonb @&gt;</c> containment operator that
/// only the Npgsql provider translates <c>EF.Functions.JsonContains</c> to; InMemory would throw
/// or no-op. Real <c>crm</c> superuser connection (RLS bypassed — SQL correctness under test,
/// not tenant isolation); unique per-run supplier names so assertions ignore any pre-existing
/// public suppliers in the dev DB. Same connection/skip/cleanup pattern as
/// <see cref="ItemRepositoryGetByAnyBarcodeIntegrationTests"/>.
/// </summary>
public sealed class MarketplaceRepositoryCoverageFilterIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = TestPostgres.DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private readonly string _run = Guid.NewGuid().ToString("N");

    // Buyer filters on "UA-32" (Київська область). Regions: UA-32 name = "Київська".
    private Guid _servedYes;                // DeliveryCoverage.served has UA-32
    private Guid _notServed;                // DeliveryCoverage.notServed has UA-32
    private Guid _servedAndNotServed;       // UA-32 in BOTH lists — notServed guard must win (exclude)
    private Guid _legacyFallbackByName;     // DeliveryCoverage NULL, Region ILIKE "%Київська%"
    private Guid _legacyFallbackByCode;     // DeliveryCoverage NULL, Region ILIKE "%UA-32%"
    private Guid _legacyNoMatch;            // DeliveryCoverage NULL, Region unrelated
    private Guid _coverageOtherRegion;      // DeliveryCoverage set but for UA-46 — fallback must NOT kick in

    public MarketplaceRepositoryCoverageFilterIntegrationTests(ITestOutputHelper output) => _output = output;

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
                $"Skipping marketplace coverage-filter integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create($"Coverage Filter Repo Test {_run}", $"coverage-filter-repo-test-{_run}");
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        _servedYes           = Seed(db, "served-yes",
            coverage: """{"served":[{"regionCode":"UA-32","terms":"2 дні"}],"notServed":[],"note":null}""");
        _notServed           = Seed(db, "not-served",
            coverage: """{"served":[{"regionCode":"UA-30","terms":null}],"notServed":["UA-32"],"note":null}""");
        _servedAndNotServed  = Seed(db, "served-and-notserved",
            coverage: """{"served":[{"regionCode":"UA-32","terms":"т"}],"notServed":["UA-32"],"note":null}""");
        _legacyFallbackByName = Seed(db, "legacy-name", coverage: null, region: "Київська область");
        _legacyFallbackByCode = Seed(db, "legacy-code", coverage: null, region: "Возимо у регіон UA-32 та сусідні");
        _legacyNoMatch        = Seed(db, "legacy-nomatch", coverage: null, region: "Львів, Львівська область");
        _coverageOtherRegion  = Seed(db, "coverage-other",
            coverage: """{"served":[{"regionCode":"UA-46","terms":null}],"notServed":[],"note":null}""",
            region: "Київська область"); // region would match the fallback, but coverage is non-null → fallback skipped

        // Catalog items so SearchSuppliersAsync (item-name pre-filter) has something to match.
        foreach (var supplierId in new[] { _servedYes, _notServed })
            db.SupplierItems.Add(new SupplierItem
            {
                SupplierId  = supplierId,
                TenantId    = _tenantId,
                CustomName  = $"Молоко {_run}",
                IsAvailable = true,
            });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_profiles WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM suppliers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task GetPublicSuppliersAsync_RegionCode_MatchesServedCoverageAndLegacyFallback_ExcludesNotServed()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var rows = await repo.GetPublicSuppliersAsync("UA-32", null, null, 1, 500);
        var mine = rows.Where(r => r.Supplier.TenantId == _tenantId).Select(r => r.Supplier.Id).ToHashSet();

        Assert.Contains(_servedYes, mine);              // served: yes
        Assert.Contains(_legacyFallbackByName, mine);   // legacy fallback via region name
        Assert.Contains(_legacyFallbackByCode, mine);   // legacy fallback via raw code

        Assert.DoesNotContain(_notServed, mine);            // notServed
        Assert.DoesNotContain(_servedAndNotServed, mine);   // in both → notServed guard wins
        Assert.DoesNotContain(_legacyNoMatch, mine);        // legacy, unrelated region
        Assert.DoesNotContain(_coverageOtherRegion, mine);  // has coverage for another region → no fallback
    }

    [Fact]
    public async Task CountPublicSuppliersAsync_RegionCode_CountsSameSetAsList()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var listed = (await repo.GetPublicSuppliersAsync("UA-32", null, null, 1, 500)).Count;
        var counted = await repo.CountPublicSuppliersAsync("UA-32", null, null);

        Assert.Equal(listed, counted);
    }

    [Fact]
    public async Task SearchSuppliersAsync_RegionCode_AppliesTheSameCoverageMatch()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var mine = (await repo.SearchSuppliersAsync($"Молоко {_run}", "UA-32"))
            .Where(r => r.Supplier.TenantId == _tenantId)
            .Select(r => r.Supplier.Id)
            .ToHashSet();

        // Both seeded items match the name; only the one whose coverage serves UA-32 survives.
        Assert.Contains(_servedYes, mine);
        Assert.DoesNotContain(_notServed, mine);
    }

    private Guid Seed(AppDbContext db, string tag, string? coverage, string? region = null)
    {
        var supplier = new Supplier { TenantId = _tenantId, Name = $"Coverage {tag} {_run}" };
        db.Suppliers.Add(supplier);
        db.SupplierProfiles.Add(new SupplierProfile
        {
            SupplierId       = supplier.Id,
            TenantId         = _tenantId,
            IsPublic         = true,
            Region           = region,
            DeliveryCoverage = coverage,
        });
        return supplier.Id;
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
