using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data.Repositories;

namespace ShelfGuard.Tests.Erp;

internal sealed record InventoryFenceScenario(Guid Tenant, Guid Location, Guid Actor, Guid Item, Guid Batch)
{
    internal InventoryAdjustmentPlan Plan(decimal before = 3.25m, decimal after = 5m) => InventoryAdjustmentPlanner.Prepare(
        Tenant, Actor, new(Guid.NewGuid(), Location, Batch, before, after, "Reviewed physical count"));

    internal static async Task<InventoryFenceScenario> Seed(ErpSchemaFixture fixture, bool activate = true)
    {
        var tenant = ShelfGuard.Domain.Entities.Tenant.Create("W4B disposable", Guid.NewGuid().ToString("N")); tenant.UpdateModules(["inventory"]);
        var location = new Location { TenantId = tenant.Id, Name = "W4B store" };
        var item = new Item { TenantId = tenant.Id, Name = "Synthetic flour", Unit = "kg" };
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@test.local", "W4B manager", "hash", "store_manager");
        var stock = new ProductStock { TenantId = tenant.Id, StoreId = location.Id, ProductId = item.Id,
            Quantity = 3.25m, QuantityInitial = 3.25m, BatchNumber = "w4b-lot", ExpiryDate = new(2027, 12, 1) };
        await using (var db = fixture.Context())
        {
            db.AddRange(tenant, location, item, user, stock, UserLocation.Create(tenant.Id, user.Id, location.Id, null)); await db.SaveChangesAsync();
        }
        await using (var db = fixture.Context())
        {
            var snapshot = await new InventorySnapshotReader(db).ReadAsync(tenant.Id, location.Id, operatorId: user.Id);
            var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
            await new ControlBalanceMaterializer(db).MaterializeAsync(new(tenant.Id, location.Id, Guid.NewGuid(),
                report.SourceFingerprint, [stock.Id], "Reviewed synthetic baseline", "w4b-disposable", OperatorId: user.Id));
            if (activate) await InventoryFenceTestHelper.Activate(db, tenant.Id, location.Id, user.Id);
        }
        return new(tenant.Id, location.Id, user.Id, item.Id, stock.Id);
    }
}
