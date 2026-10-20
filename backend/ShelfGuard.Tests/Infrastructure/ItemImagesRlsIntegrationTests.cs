using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres proof of the <c>item_images</c> RLS (tenant_isolation, worker_bypass) and the
/// one-primary-per-item partial unique index, via the non-superuser <c>rls_audit_test_role</c>.
/// Soft-skips when no Postgres is reachable or the AddItemImages migration is not applied.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class ItemImagesRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;
    private bool _available;

    public ItemImagesRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping item_images RLS tests - no Postgres: {_fixture.UnavailableReason}");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource).IgnoreManyServiceProvidersWarning().Options;

        await using var db = new AppDbContext(_options);
        var exists = await db.Database
            .SqlQueryRaw<bool>("SELECT to_regclass('public.item_images') IS NOT NULL AS \"Value\"")
            .SingleAsync();
        if (!exists)
        {
            _output.WriteLine("Skipping item_images RLS tests - migration AddItemImages not applied to this DB.");
            return;
        }

        await db.Database.ExecuteSqlRawAsync("GRANT ALL PRIVILEGES ON item_images TO rls_audit_test_role;");
        _available = true;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    private async Task<(Tenant A, Tenant B, Item ItemA)> SeedAsync()
    {
        await using var db = new AppDbContext(_options!);
        var a = Tenant.Create($"Img RLS A {Guid.NewGuid():N}", $"img-rls-a-{Guid.NewGuid():N}");
        var b = Tenant.Create($"Img RLS B {Guid.NewGuid():N}", $"img-rls-b-{Guid.NewGuid():N}");
        db.Tenants.AddRange(a, b);
        await db.SaveChangesAsync();

        Item? itemA = null;
        foreach (var t in new[] { a, b })
        {
            var item = new Item { TenantId = t.Id, Name = "Milk" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            db.ItemImages.Add(new ItemImage { TenantId = t.Id, ItemId = item.Id, ImageUrl = "/uploads/items/x.jpg", IsPrimary = true });
            await db.SaveChangesAsync();
            if (t == a) itemA = item;
        }
        return (a, b, itemA!);
    }

    private async Task CleanupAsync(Tenant a, Tenant b)
    {
        await using var db = new AppDbContext(_options!);
        foreach (var t in new[] { a.Id, b.Id })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM item_images WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {t}");
        }
    }

    private async Task<int> CountAsync(string tenantId, string role)
    {
        await using var db = new AppDbContext(_options!);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role;");
        if (tenantId.Length > 0) await db.Database.ExecuteSqlRawAsync($"SET app.tenant_id = '{tenantId}';");
        if (role.Length > 0) await db.Database.ExecuteSqlRawAsync($"SET app.role = '{role}';");
        try
        {
            return await db.ItemImages.CountAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
    }

    [Fact]
    public async Task Tenant_sees_only_own_images_and_anonymous_sees_none()
    {
        if (!_available) return;
        var (a, b, _) = await SeedAsync();
        try
        {
            Assert.Equal(1, await CountAsync(a.Id.ToString(), "store_manager"));
            Assert.Equal(1, await CountAsync(b.Id.ToString(), "store_manager"));
            Assert.Equal(0, await CountAsync("", ""));
        }
        finally
        {
            await CleanupAsync(a, b);
        }
    }

    [Fact]
    public async Task Worker_role_sees_all_tenants()
    {
        if (!_available) return;
        var (a, b, _) = await SeedAsync();
        try
        {
            Assert.True(await CountAsync("", "worker") >= 2);
        }
        finally
        {
            await CleanupAsync(a, b);
        }
    }

    [Fact]
    public async Task Second_primary_for_same_item_is_rejected_by_partial_unique_index()
    {
        if (!_available) return;
        var (a, b, item) = await SeedAsync();
        try
        {
            await using var db = new AppDbContext(_options!);
            db.ItemImages.Add(new ItemImage { TenantId = a.Id, ItemId = item.Id, ImageUrl = "/uploads/items/y.jpg", IsPrimary = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var db2 = new AppDbContext(_options!);
            db2.ItemImages.Add(new ItemImage { TenantId = a.Id, ItemId = item.Id, ImageUrl = "/uploads/items/z.jpg", IsPrimary = false, SortOrder = 1 });
            await db2.SaveChangesAsync(); // non-primary extras are fine
        }
        finally
        {
            await CleanupAsync(a, b);
        }
    }
}
