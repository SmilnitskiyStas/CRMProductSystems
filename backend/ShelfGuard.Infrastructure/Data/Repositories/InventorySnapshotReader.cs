using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShelfGuard.Application.Features.Erp.Backfill;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>A bounded, tenant-filtered, read-only MVCC snapshot. It deliberately has no Save/apply path.</summary>
public sealed class InventorySnapshotReader(AppDbContext db) : IInventorySnapshotReader
{
    public async Task<InventorySnapshot> ReadAsync(Guid tenantId, Guid locationId, int maxRows = 20_000, CancellationToken ct = default, Guid? operatorId = null)
        => await ReadCoreAsync(tenantId, locationId, maxRows, null, ct, operatorId);

    /// <summary>Explicit maintenance opt-in. All sale lines remain in the fingerprint;
    /// other streams and the ordinary read API retain their original limits.</summary>
    public Task<InventorySnapshot> ReadControlOpeningAsync(Guid tenantId, Guid locationId, int maxRows, int maxSaleRows,
        CancellationToken ct = default, Guid? operatorId = null) =>
        ReadCoreAsync(tenantId, locationId, maxRows, maxSaleRows, ct, operatorId);

    private async Task<InventorySnapshot> ReadCoreAsync(Guid tenantId, Guid locationId, int maxRows, int? maxSaleRows, CancellationToken ct, Guid? operatorId)
    {
        if (tenantId == Guid.Empty || locationId == Guid.Empty || maxRows is < 1 or > 100_000)
            throw new ArgumentException("Explicit tenant/location and a row limit between 1 and 100000 are required.");
        if (maxSaleRows is { } salesLimit && (salesLimit < maxRows || salesLimit > 500_000))
            throw new ArgumentException("Maintenance sales limit must be explicit, at least maxRows and at most 500000.");
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Inventory preflight requires a clean PostgreSQL context and its own read-only transaction.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var snapshot = await ReadInTransactionAsync(tenantId, locationId, maxRows, ct, operatorId, maxSaleRows);
        await transaction.CommitAsync(ct);
        return snapshot;
    }

    // Only the maintenance writer shares this path, after locking every source/target table.
    // The public read API still owns and enforces a read-only transaction.
    internal async Task<InventorySnapshot> ReadInTransactionAsync(Guid tenantId, Guid locationId, int maxRows, CancellationToken ct, Guid? operatorId = null, int? maxSaleRows = null)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("A snapshot transaction is required.");
        await EstablishScopeAsync(tenantId, locationId, ct, operatorId);

        DateTime capturedAt; string snapshotId;
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT CURRENT_TIMESTAMP, pg_current_snapshot()::text";
            await using var result = await command.ExecuteReaderAsync(ct);
            await result.ReadAsync(ct); capturedAt = result.GetDateTime(0); snapshotId = result.GetString(1);
        }

        return await ReadRowsAsync(tenantId, locationId, maxRows, capturedAt, snapshotId, ct, maxSaleRows);
    }

    internal async Task EstablishScopeAsync(Guid tenantId, Guid locationId, CancellationToken ct, Guid? operatorId = null)
    {
        // Never inherit provider/worker/consumer bypass from a pooled connection or caller.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id',{tenantId.ToString()},true), set_config('app.role','',true), set_config('app.user_id',{operatorId?.ToString() ?? ""},true), set_config('app.consumer_account_id','',true)", ct);
        if (!await db.Locations.AsNoTracking().AnyAsync(l => l.TenantId == tenantId && l.Id == locationId, ct))
            throw new UnauthorizedAccessException("Requested location is not visible in this tenant scope.");
        if (operatorId.HasValue)
        {
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == operatorId && u.TenantId == tenantId && u.IsActive, ct)
                || !await db.UserLocations.AsNoTracking().AnyAsync(ul => ul.TenantId == tenantId && ul.UserId == operatorId && ul.LocationId == locationId, ct))
                throw new UnauthorizedAccessException("Operator is not an active tenant user assigned to the requested location.");
        }
        else if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND permissive='RESTRICTIVE'
              AND tablename IN ('product_stock','stock_movements','pos_transactions','production_orders')) AS "Value"
            """).SingleAsync(ct))
            throw new UnauthorizedAccessException("Explicit assigned operator is required by legacy location RLS; an empty report is not acceptance.");
    }

    private async Task<InventorySnapshot> ReadRowsAsync(Guid tenantId, Guid locationId, int maxRows, DateTime capturedAt, string snapshotId, CancellationToken ct, int? maxSaleRows)
    {
        async Task<List<T>> Rows<T>(IQueryable<T> query, string stream, int? explicitLimit = null)
        {
            var limit = explicitLimit ?? maxRows;
            var rows = await query.Take(limit + 1).ToListAsync(ct);
            if (rows.Count > limit) throw new SnapshotLimitExceededException(stream, limit);
            return rows;
        }
        var ownBatches = db.ProductStocks.AsNoTracking().Where(b => b.TenantId == tenantId);
        var scopedBatches = ownBatches.Where(b => b.StoreId == locationId);
        var batches = await Rows(scopedBatches.OrderBy(b => b.Id).Select(b => new SnapshotBatch(b.Id, b.TenantId, b.ProductId,
            b.StoreId, b.ZoneId, b.Quantity, b.QuantityInitial, b.AddedAt, b.SourceType, b.SourceId, b.BatchNumber, b.ExpiryDate)), "batches");
        var movements = await Rows(db.StockMovements.AsNoTracking().Where(m => m.TenantId == tenantId
            && (scopedBatches.Any(b => b.Id == m.ProductStockId)
                || !ownBatches.Any(b => b.Id == m.ProductStockId) && (m.FromStoreId == locationId || m.ToStoreId == locationId
                    || m.FromStoreId == null && m.ToStoreId == null)))
            .OrderBy(m => m.Id).Select(m => new SnapshotMovement(m.Id, m.TenantId, m.ProductId, m.ProductStockId, m.MovementType,
                m.FromStoreId, m.ToStoreId, m.Quantity, m.QuantityBefore, m.QuantityAfter, m.ReferenceType, m.ReferenceId, m.CreatedAt)), "movements");
        var events = await Rows(db.StockEvents.AsNoTracking().Where(e => e.TenantId == tenantId && e.QuantityDelta != null && e.QuantityDelta != 0
            && (scopedBatches.Any(b => b.Id == e.ProductStockId)
                || (e.StoreId == locationId || e.StoreId == null) && !ownBatches.Any(b => b.Id == e.ProductStockId)))
            .OrderBy(e => e.Id).Select(e => new SnapshotEvent(e.Id, e.TenantId, e.ProductId, e.ProductStockId, e.StoreId,
                e.EventType, e.QuantityDelta, e.Meta, e.CreatedAt)), "quantity_events");
        var sales = await Rows(db.PosTransactionItems.AsNoTracking()
            .Join(db.PosTransactions.AsNoTracking().Where(t => t.TenantId == tenantId && t.StoreId == locationId),
                i => i.TransactionId, t => t.Id, (i, t) => new { Item = i, Transaction = t })
            .OrderBy(x => x.Item.Id).Select(x => new SnapshotSale(x.Item.Id, x.Transaction.TenantId, x.Transaction.Id,
                x.Item.ProductId, x.Transaction.StoreId, x.Item.ProductStockId, x.Item.Quantity)), "sale_lines", maxSaleRows);
        var production = await Rows(db.ProductionOrderConsumptions.AsNoTracking()
            .Join(db.ProductionOrders.AsNoTracking().Where(o => o.TenantId == tenantId && o.LocationId == locationId),
                c => c.ProductionOrderId, o => o.Id, (c, o) => new { Consumption = c, Order = o })
            .OrderBy(x => x.Consumption.Id).Select(x => new SnapshotProduction(x.Consumption.Id, x.Order.TenantId, x.Order.Id,
                x.Consumption.ItemId, x.Order.LocationId, x.Consumption.ProductStockId, x.Consumption.QtyConsumed)), "production_consumption");
        var ledger = await Rows(db.InventoryLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenantId && e.LocationId == locationId)
            .OrderBy(e => e.Id).Select(e => new SnapshotLedger(e.Id, e.TenantId, e.ItemId, e.LocationId, e.BatchId, e.QuantityDelta)), "ledger");
        var projections = await Rows(db.InventoryBalanceProjections.AsNoTracking().Where(p => p.TenantId == tenantId && p.LocationId == locationId)
            .OrderBy(p => p.Id).Select(p => new SnapshotProjection(p.Id, p.TenantId, p.ItemId, p.LocationId, p.BatchId, p.Quantity, p.ReservedQuantity)), "projections");
        var itemIds = batches.Select(b => b.ItemId).Concat(sales.Select(s => s.ItemId)).Concat(ledger.Select(e => e.ItemId))
            .Concat(production.Select(p => p.ItemId)).Distinct().ToArray();
        var items = await Rows(db.Items.AsNoTracking().Where(i => i.TenantId == tenantId && itemIds.Contains(i.Id))
            .OrderBy(i => i.Id).Select(i => new SnapshotItem(i.Id, i.TenantId, i.Name, i.Unit)), "items");
        var zones = await Rows(db.LocationZones.AsNoTracking().Where(z => z.LocationId == locationId)
            .OrderBy(z => z.Id).Select(z => new SnapshotZone(z.Id, z.LocationId)), "zones");
        return new(tenantId, locationId, capturedAt, snapshotId, items, zones, batches, movements, events, sales, production, ledger, projections);
    }
}
