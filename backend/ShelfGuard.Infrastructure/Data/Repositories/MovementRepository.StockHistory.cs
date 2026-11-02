using System.Data;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed partial class MovementRepository
{
    public async Task<StockHistorySnapshot> GetStockHistoryAsync(
        Guid tenantId, Guid productId, Guid? storeId, DateOnly from, CancellationToken ct = default)
    {
        // All queries see the same committed sale: its balance and receipt must not be read
        // from different snapshots while a checkout is completing.
        await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            : null;
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var batches = _db.ProductStocks.AsNoTracking().Where(b =>
            b.TenantId == tenantId && b.ProductId == productId && (!storeId.HasValue || b.StoreId == storeId));
        // Include depleted/archived batches: they still belong to the historical balance.
        var current = await batches.SumAsync(b => (decimal?)b.Quantity, ct) ?? 0m;

        // QuantityBefore/After describe ONE batch, never the total product balance.
        // Joining the affected batch also distinguishes outbound/inbound transfer rows:
        // both rows carry both store IDs, so filtering those IDs would double-count them.
        var movements = _db.StockMovements.AsNoTracking().Where(m =>
            m.TenantId == tenantId && m.ProductId == productId);
        var movementChanges = await movements
            .Where(m => m.CreatedAt >= fromUtc && m.QuantityBefore != null && m.QuantityAfter != null)
            // ERP POS also writes a compatible movement; the durable receipt below already counts that sale.
            .Where(m => !(m.MovementType == "sale" && m.ReferenceType == "pos_sale"
                && _db.PosTransactions.Any(t => t.TenantId == tenantId && t.Id == m.ReferenceId && t.Status != "cancelled")))
            .Join(batches, m => m.ProductStockId, b => (Guid?)b.Id, (m, b) => m)
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Delta = g.Sum(m => m.QuantityAfter!.Value - m.QuantityBefore!.Value) })
            .ToListAsync(ct);

        // POS receipts are durable; pos_sale stock_events expire during cleanup. Use the
        // receipt once (not both sources), even if fiscalization failed after FEFO succeeded.
        var sales = await _db.PosTransactionItems.AsNoTracking().Where(i => i.ProductId == productId)
            .Join(_db.PosTransactions.Where(t => t.TenantId == tenantId && t.Status != "cancelled"
                    && t.CreatedAt >= fromUtc && (!storeId.HasValue || t.StoreId == storeId)),
                i => i.TransactionId, t => t.Id, (i, t) => new { t.CreatedAt, i.Quantity })
            .GroupBy(s => s.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Delta = -g.Sum(s => s.Quantity) })
            .ToListAsync(ct);

        // Production-created/legacy batches may have no receipt movement. Count their
        // initial addition once; ordinary receipt/transfer batches already have a ledger row.
        var additions = await batches.Where(b => b.AddedAt >= fromUtc
                && !movements.Any(m => m.ProductStockId == b.Id && m.QuantityBefore == 0
                    && (m.MovementType == "receipt" || m.MovementType == "transfer")
                    // Restoring a depleted source batch is not its initial receipt.
                    && m.ReferenceType != "stock_transfer_cancellation"))
            .GroupBy(b => b.AddedAt.Date)
            .Select(g => new { Date = g.Key, Delta = g.Sum(b => b.QuantityInitial) })
            .ToListAsync(ct);
        var consumption = await _db.StockEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ProductId == productId && e.CreatedAt >= fromUtc
                && (e.EventType == "production_consumption" || e.EventType == "auto_service_consumption"))
            .Where(e=>e.EventType!="production_consumption"||!_db.StockMovements.Any(m=>m.TenantId==tenantId&&m.ReferenceType=="production_consumption_event"&&m.ReferenceId==e.Id))
            .Join(batches, e => e.ProductStockId, b => (Guid?)b.Id, (e, b) => e)
            .GroupBy(e => e.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Delta = g.Sum(e => e.QuantityDelta ?? 0m) })
            .ToListAsync(ct);
        var changes = movementChanges.Concat(sales).Concat(additions).Concat(consumption)
            .GroupBy(p => DateOnly.FromDateTime(p.Date)).ToDictionary(g => g.Key, g => g.Sum(p => p.Delta));
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new StockHistorySnapshot(current, DateOnly.FromDateTime(DateTime.UtcNow), changes);
    }
}
