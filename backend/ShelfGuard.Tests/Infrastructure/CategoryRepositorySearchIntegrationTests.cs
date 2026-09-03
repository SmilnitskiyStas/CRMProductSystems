using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Supplier-portal expansion #8 (Phase 6e): live-Postgres coverage for
/// <see cref="CategoryRepository.SearchActiveAsync"/> — the <c>Name ILIKE '%term%'</c> typeahead
/// over <c>platform_categories</c>. Needs real Postgres for the Npgsql-only
/// <c>EF.Functions.ILike</c> (InMemory can't translate it — same limitation documented on
/// <c>ItemRepositoryGetPagedTests</c>). Plain <c>crm</c> connection; a unique per-run marker in
/// every category name so assertions ignore any pre-existing rows.
/// </summary>
public sealed class CategoryRepositorySearchIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = TestPostgres.DefaultConnectionString;
    private bool _dbAvailable;
    private readonly string _run = Guid.NewGuid().ToString("N")[..10];

    private Guid _tenantA;
    private Guid _tenantB;
    private Guid _parentId;
    private Guid _dairyId;

    public CategoryRepositorySearchIntegrationTests(ITestOutputHelper output) => _output = output;

    private string Tag(string name) => $"{name} {_run}";

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
            _output.WriteLine($"Skipping category-search repo integration tests — no reachable Postgres: {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenantA = Tenant.Create($"CatSearch A {_run}", $"cat-search-a-{_run}");
        var tenantB = Tenant.Create($"CatSearch B {_run}", $"cat-search-b-{_run}");
        _tenantA = tenantA.Id;
        _tenantB = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var parent = new PlatformCategory { Name = Tag("Продукти"), IsActive = true };
        var dairy = new PlatformCategory { Name = Tag("Молочні продукти"), ParentId = parent.Id, IsActive = true };
        var meat = new PlatformCategory { Name = Tag("Мʼясо"), ParentId = parent.Id, IsActive = true };
        var archived = new PlatformCategory { Name = Tag("Молочний архів"), IsActive = false };
        _parentId = parent.Id;
        _dairyId = dairy.Id;
        db.PlatformCategories.AddRange(parent, dairy, meat, archived);

        // Per-tenant item count: 2 items for tenant A in dairy, 1 for tenant B in dairy.
        db.Items.AddRange(
            new Item { TenantId = _tenantA, Name = "Молоко", ManagementType = "MTS", CategoryId = dairy.Id },
            new Item { TenantId = _tenantA, Name = "Кефір", ManagementType = "MTS", CategoryId = dairy.Id },
            new Item { TenantId = _tenantB, Name = "Сир", ManagementType = "MTS", CategoryId = dairy.Id });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        foreach (var tid in new[] { _tenantA, _tenantB })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {tid}");
        }
        // Children (ParentId RESTRICT) before parents.
        var like = "%" + _run;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform_categories WHERE \"ParentId\" IS NOT NULL AND \"Name\" LIKE {like}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform_categories WHERE \"Name\" LIKE {like}");
    }

    [Fact]
    public async Task SearchActiveAsync_MatchesCaseInsensitively_ExcludesInactive_ResolvesParentName()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new CategoryRepository(db);

        var rows = await repo.SearchActiveAsync(_tenantA, "МОЛОЧ", 20);

        // "Молочні продукти" matches; "Молочний архів" is inactive → excluded.
        var hit = Assert.Single(rows, r => r.Id == _dairyId);
        Assert.Equal(Tag("Молочні продукти"), hit.Name);
        Assert.Equal(Tag("Продукти"), hit.ParentName);
        Assert.DoesNotContain(rows, r => r.Name.Contains("архів"));
    }

    [Fact]
    public async Task SearchActiveAsync_ItemCountIsScopedToTheCallerTenant()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new CategoryRepository(db);

        var forA = Assert.Single(await repo.SearchActiveAsync(_tenantA, "Молочні продукти " + _run, 20));
        Assert.Equal(2, forA.ItemCount);

        var forB = Assert.Single(await repo.SearchActiveAsync(_tenantB, "Молочні продукти " + _run, 20));
        Assert.Equal(1, forB.ItemCount);
    }

    [Fact]
    public async Task SearchActiveAsync_BlankTerm_ReturnsActiveOnly_OrderedByName_RespectingLimit()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new CategoryRepository(db);

        var limited = await repo.SearchActiveAsync(_tenantA, term: "  ", limit: 2);
        Assert.Equal(2, limited.Count);

        var all = await repo.SearchActiveAsync(_tenantA, term: null, limit: 500);
        var mine = all.Where(r => r.Name.EndsWith(_run)).Select(r => r.Name).ToList();
        Assert.Equal(new[] { Tag("Молочні продукти"), Tag("Мʼясо"), Tag("Продукти") }.OrderBy(x => x, StringComparer.Ordinal),
            mine.OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain(mine, n => n.Contains("архів"));   // inactive never returned
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
