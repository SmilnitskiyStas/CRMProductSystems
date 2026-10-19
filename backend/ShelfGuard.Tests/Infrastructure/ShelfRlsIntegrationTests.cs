using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres proof of the <c>shelf_items</c> / <c>shelf_scans</c> RLS: tenant_isolation,
/// worker_bypass and the RESTRICTIVE store_scope (user_locations) policy, exercised through the
/// EF model under the non-superuser <c>rls_audit_test_role</c>.
/// Soft-skips when no Postgres is reachable OR the AddShelfItemsAndScans migration has not been
/// applied to that database (we never apply migrations to a shared DB from tests).
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class ShelfRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;
    private bool _available;

    public ShelfRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping shelf RLS tests — no Postgres: {_fixture.UnavailableReason}");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource).IgnoreManyServiceProvidersWarning().Options;

        await using var db = new AppDbContext(_options);
        var exists = await db.Database
            .SqlQueryRaw<bool>("SELECT to_regclass('public.shelf_items') IS NOT NULL AND to_regclass('public.shelf_scans') IS NOT NULL AS \"Value\"")
            .SingleAsync();
        if (!exists)
        {
            _output.WriteLine("Skipping shelf RLS tests — migration AddShelfItemsAndScans not applied to this DB.");
            return;
        }

        // Regrant: the fixture ran GRANT ... ON ALL TABLES before our tables might have existed.
        await db.Database.ExecuteSqlRawAsync("GRANT ALL PRIVILEGES ON shelf_items, shelf_scans TO rls_audit_test_role;");
        _available = true;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    private sealed record Seed(Tenant TenantA, Tenant TenantB, Location LocA, Location LocB, User UserA);

    private async Task<Seed> SeedAsync()
    {
        await using var db = new AppDbContext(_options!);
        var a = Tenant.Create($"Shelf RLS A {Guid.NewGuid():N}", $"shelf-rls-a-{Guid.NewGuid():N}");
        var b = Tenant.Create($"Shelf RLS B {Guid.NewGuid():N}", $"shelf-rls-b-{Guid.NewGuid():N}");
        db.Tenants.AddRange(a, b);
        await db.SaveChangesAsync();

        var locA = new Location { TenantId = a.Id, Name = "A" };
        var locB = new Location { TenantId = b.Id, Name = "B" };
        db.Locations.AddRange(locA, locB);
        var user = User.Create(a.Id, $"shelf-{Guid.NewGuid():N}@example.com", "U", "hash", "store_manager");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        foreach (var (t, l) in new[] { (a, locA), (b, locB) })
        {
            var zone = new LocationZone { LocationId = l.Id, Name = "Z" };
            var item = new Item { TenantId = t.Id, Name = "Milk" };
            db.LocationZones.Add(zone);
            db.Items.Add(item);
            await db.SaveChangesAsync();
            db.ShelfItems.Add(new ShelfItem { TenantId = t.Id, LocationId = l.Id, ZoneId = zone.Id, ShelfId = Guid.NewGuid(), ItemId = item.Id });
            db.ShelfScans.Add(new ShelfScan { TenantId = t.Id, LocationId = l.Id, ZoneId = zone.Id, ImagePath = "/x.jpg" });
            await db.SaveChangesAsync();
        }

        return new Seed(a, b, locA, locB, user);
    }

    private async Task CleanupAsync(Seed s)
    {
        await using var db = new AppDbContext(_options!);
        foreach (var t in new[] { s.TenantA.Id, s.TenantB.Id })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shelf_scans WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shelf_items WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_locations WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM item_zone_assignments WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM location_zones WHERE \"LocationId\" IN (SELECT \"Id\" FROM locations WHERE \"TenantId\" = {t})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {t}");
        }
    }

    private async Task<(int Items, int Scans)> CountAsync(string tenantId, string role, string? userId)
    {
        await using var db = new AppDbContext(_options!);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role; RESET app.user_id;");
        if (tenantId.Length > 0) await db.Database.ExecuteSqlRawAsync($"SET app.tenant_id = '{tenantId}';");
        if (role.Length > 0) await db.Database.ExecuteSqlRawAsync($"SET app.role = '{role}';");
        if (userId is not null) await db.Database.ExecuteSqlRawAsync($"SET app.user_id = '{userId}';");
        try
        {
            var i = await db.ShelfItems.CountAsync();
            var sc = await db.ShelfScans.CountAsync();
            return (i, sc);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
    }

    [Fact]
    public async Task Tenant_sees_only_own_rows_and_store_scope_narrows_by_user_locations()
    {
        if (!_available) return;
        var s = await SeedAsync();
        try
        {
            // enterprise_admin bypasses store_scope but tenant_isolation still isolates tenants.
            var admin = await CountAsync(s.TenantA.Id.ToString(), "enterprise_admin", s.UserA.Id.ToString());
            Assert.Equal((1, 1), admin);

            // A store_manager with no user_locations row sees nothing (fail-closed).
            var unassigned = await CountAsync(s.TenantA.Id.ToString(), "store_manager", s.UserA.Id.ToString());
            Assert.Equal((0, 0), unassigned);

            // After assignment to the location the rows appear — still only tenant A's.
            await using (var db = new AppDbContext(_options!))
            {
                db.UserLocations.Add(UserLocation.Create(s.TenantA.Id, s.UserA.Id, s.LocA.Id, null));
                await db.SaveChangesAsync();
            }
            var assigned = await CountAsync(s.TenantA.Id.ToString(), "store_manager", s.UserA.Id.ToString());
            Assert.Equal((1, 1), assigned);

            // No tenant context at all -> nothing.
            var anon = await CountAsync("", "", null);
            Assert.Equal((0, 0), anon);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    [Fact]
    public async Task Worker_role_sees_all_tenants()
    {
        if (!_available) return;
        var s = await SeedAsync();
        try
        {
            var (items, scans) = await CountAsync("", "worker", null);
            Assert.True(items >= 2);
            Assert.True(scans >= 2);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }
}
