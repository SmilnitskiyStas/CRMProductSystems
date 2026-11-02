using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class StagingInventoryScopePostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresFact]
    public async Task Missing_operator_fails_closed_instead_of_reporting_zero_batches_under_legacy_RLS()
    {
        var s = await SeedAsync(); await EnableAsync();
        try
        {
            await using var db = await ScopedAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location));
        }
        finally { await DisableAsync(); }
    }

    [ErpPostgresTheory]
    [InlineData("foreign")]
    [InlineData("unassigned")]
    [InlineData("inactive")]
    public async Task Operator_must_be_active_and_assigned_in_the_same_tenant(string invalid)
    {
        var s = await SeedAsync(); var other = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            if (invalid == "unassigned") setup.UserLocations.RemoveRange(await setup.UserLocations.Where(x => x.UserId == s.Operator).ToArrayAsync());
            if (invalid == "inactive")
                await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET \"IsActive\"=false WHERE \"Id\"={s.Operator}");
            await setup.SaveChangesAsync();
        }
        await EnableAsync();
        try
        {
            await using var db = await ScopedAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location,
                operatorId: invalid == "foreign" ? other.Operator : s.Operator));
        }
        finally { await DisableAsync(); }
    }

    [ErpPostgresFact]
    public async Task Assigned_operator_reads_all_own_batches_without_worker_bypass_and_excludes_other_tenant()
    {
        var s = await SeedAsync(); var other = await SeedAsync(); await EnableAsync();
        try
        {
            await using var db = await ScopedAsync();
            var snapshot = await new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location, operatorId: s.Operator);
            Assert.Equal(s.Batch, Assert.Single(snapshot.Batches).Id); Assert.DoesNotContain(snapshot.Batches, b => b.Id == other.Batch);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventorySnapshotReader(db).ReadAsync(s.Tenant, other.Location, operatorId: s.Operator));
        }
        finally { await DisableAsync(); }
    }

    [ErpPostgresFact]
    public async Task Maintenance_writer_uses_verified_operator_and_preserves_it_in_posting_provenance()
    {
        var s = await SeedAsync(); await EnableAsync();
        try
        {
            await using var db = await ScopedAsync();
            var snapshot = await new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location, operatorId: s.Operator);
            var request = new ControlBalanceRequest(s.Tenant, s.Location, Guid.NewGuid(), new InventoryDryRunAnalyzer().Analyze(snapshot).SourceFingerprint,
                [s.Batch], "Synthetic staging role acceptance", "synthetic-legacy-rls", OperatorId: s.Operator);
            var first = await new ControlBalanceMaterializer(db).MaterializeAsync(request);
            Assert.True((await new ControlBalanceMaterializer(db).MaterializeAsync(request)).Replayed);
            await using var verify = fixture.Context();
            Assert.Equal(s.Operator, (await verify.BusinessDocuments.SingleAsync(x => x.Id == first.Batches[0].DocumentId)).CreatedBy);
            Assert.Equal(s.Operator, (await verify.DocumentPostings.SingleAsync(x => x.Id == first.Batches[0].PostingId)).PerformedBy);
        }
        finally { await DisableAsync(); }
    }

    private async Task<AppDbContext> ScopedAsync()
    {
        var db = fixture.Context(); await db.Database.OpenConnectionAsync();
        var role = $"SET ROLE {fixture.ReaderRole}"; await db.Database.ExecuteSqlRawAsync(role);
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        return db;
    }
    private async Task EnableAsync()
    {
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE product_stock ENABLE ROW LEVEL SECURITY;
            ALTER TABLE product_stock FORCE ROW LEVEL SECURITY;
            CREATE POLICY w3_stage_tenant ON product_stock USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
            CREATE POLICY w3_stage_location ON product_stock AS RESTRICTIVE USING (EXISTS(
              SELECT 1 FROM user_locations ul WHERE ul."UserId"=NULLIF(current_setting('app.user_id',true),'')::uuid AND ul."LocationId"=product_stock."LocationId"));
            """);
    }
    private async Task DisableAsync()
    {
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("""
            DROP POLICY w3_stage_location ON product_stock;
            DROP POLICY w3_stage_tenant ON product_stock;
            ALTER TABLE product_stock DISABLE ROW LEVEL SECURITY;
            ALTER TABLE product_stock NO FORCE ROW LEVEL SECURITY;
            """);
    }
    private async Task<Seed> SeedAsync()
    {
        var tenant = Tenant.Create("Synthetic staging scope", Guid.NewGuid().ToString("N"));
        var location = new Location { TenantId = tenant.Id, Name = "Assigned location" };
        var item = new Item { TenantId = tenant.Id, Name = "Scope item" };
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N")+"@synthetic.example", "Synthetic operator", "not-a-login-hash", "store_manager");
        var batch = new ProductStock { TenantId = tenant.Id, StoreId = location.Id, ProductId = item.Id,
            Quantity = 2, QuantityInitial = 2, ExpiryDate = new(2026, 12, 1) };
        await using var db = fixture.Context(); db.AddRange(tenant, location, item, user, batch,
            UserLocation.Create(tenant.Id, user.Id, location.Id, null)); await db.SaveChangesAsync();
        return new(tenant.Id, location.Id, batch.Id, user.Id);
    }
    private sealed record Seed(Guid Tenant, Guid Location, Guid Batch, Guid Operator);
}
