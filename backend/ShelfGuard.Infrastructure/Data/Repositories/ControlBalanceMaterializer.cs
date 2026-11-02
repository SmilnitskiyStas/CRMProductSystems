using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>Maintenance-only materializer. No API/DI registration or live writer cutover.</summary>
public sealed class ControlBalanceMaterializer(AppDbContext db) : IControlBalanceMaterializer
{
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    // Table locks also cover old writers that do not know about ERP/advisory locks, and prevent phantoms.
    // NOWAIT fails closed on active writers; locks are acquired BEFORE the first MVCC snapshot statement.
    private const string LockTables = """
        SET LOCAL search_path=pg_catalog,public;
        LOCK TABLE public.items, public.locations, public.location_zones, public.product_stock,
          public.stock_movements, public.stock_events, public.pos_transactions, public.pos_transaction_items,
          public.production_orders, public.production_order_consumptions,
          public.business_documents, public.document_lines, public.document_postings,
          public.inventory_ledger_entries, public.inventory_balance_projections,
          public.inventory_reservations, public.inventory_reconciliations, public.outbox_events
        IN SHARE ROW EXCLUSIVE MODE NOWAIT
        """;

    public async Task<ControlBalanceResult> MaterializeAsync(ControlBalanceRequest request, CancellationToken ct = default)
    {
        // Freeze a caller-owned mutable batch collection before hashing or awaiting.
        request = request with { BatchIds = request.BatchIds?.ToArray()! };
        var requestHash = ControlBalancePlanner.RequestHash(request);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Materialization requires its own clean PostgreSQL context.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        ct = deadline.Token;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(LockTables, ct);
            var reader = new InventorySnapshotReader(db);
            await reader.EstablishScopeAsync(request.TenantId, request.LocationId, ct, request.OperatorId);
            var prior = await db.InventoryReconciliations.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && x.RunId == request.RunId).Take(1001).ToListAsync(ct);
            if (prior.Count > 0)
            {
                var replay = await ReplayAsync(request, requestHash, prior, ct);
                await transaction.CommitAsync(ct);
                return replay;
            }
            var snapshot = await reader.ReadInTransactionAsync(request.TenantId, request.LocationId, request.MaxRows, ct, request.OperatorId, request.MaxSaleRows);
            var plans = ControlBalancePlanner.Plan(request, snapshot);
            var selected = request.BatchIds.ToArray();
            // Batch-less reservations also claim the same item/location. Never silently discard reserves.
            var selectedItems = plans.Select(x => x.Batch.ItemId).Distinct().ToArray();
            if (await db.InventoryReservations.AsNoTracking().AnyAsync(r => r.TenantId == request.TenantId
                && r.LocationId == request.LocationId && r.Status == "active"
                && (r.BatchId.HasValue ? selected.Contains(r.BatchId.Value) : selectedItems.Contains(r.ItemId)), ct))
                throw new MaterializationConflictException("Active reservations require separate reconciliation before control opening.");

            var at = snapshot.CapturedAt;
            var receipt = JsonSerializer.Serialize(new Receipt(ControlBalancePlanner.Version, requestHash, request.SourceFingerprint,
                request.Reason, request.AcceptanceReference), AuditJson);
            if (receipt.Length > 2000)
                throw new MaterializationConflictException("Encoded audit explanation exceeds the schema limit.");
            var documents = plans.Select(p => new BusinessDocument
            {
                Id = p.Identity.DocumentId, TenantId = request.TenantId, DocumentKey = p.Identity.DocumentKey,
                DocumentType = "control_opening_balance", SourceSystem = p.Identity.SourceSystem, SourceId = p.Identity.SourceId,
                ContentHash = requestHash, OccurredAt = at, CreatedAt = at, UpdatedAt = at, CreatedBy = request.OperatorId,
            }).ToList();
            db.AddRange(documents); await db.SaveChangesAsync(ct);
            db.AddRange(plans.Select(p => new DocumentLine
            {
                Id = p.Identity.LineId, TenantId = request.TenantId, DocumentId = p.Identity.DocumentId,
                LineNumber = 1, ItemId = p.Batch.ItemId, ToLocationId = request.LocationId, Quantity = p.Batch.Quantity,
                Unit = p.Item.Unit, ItemNameSnapshot = p.Item.Name, BatchNumberSnapshot = p.Batch.BatchNumber,
                ExpiryDateSnapshot = p.Batch.ExpiryDate,
                Metadata = JsonSerializer.Serialize(new { operation = ControlBalancePlanner.Version, request.RunId,
                    request.SourceFingerprint, request.Reason, request.AcceptanceReference, request.OperatorId,
                    snapshotKind = "control_at_capture", itemLabelKind = "current_at_capture", financialHistory = "unknown" }),
            }));
            await db.SaveChangesAsync(ct);
            foreach (var document in documents) document.Status = "submitted";
            await db.SaveChangesAsync(ct);
            foreach (var document in documents) document.Status = "approved";
            await db.SaveChangesAsync(ct);
            db.AddRange(plans.Select(p => new DocumentPosting
            {
                Id = p.Identity.PostingId, TenantId = request.TenantId, DocumentId = p.Identity.DocumentId,
                IdempotencyKey = $"{ControlBalancePlanner.Version}:{p.Batch.Id:N}", RequestHash = requestHash, PostedAt = at, PerformedBy = request.OperatorId,
            }));
            await db.SaveChangesAsync(ct);
            db.AddRange(plans.Select(p => new InventoryLedgerEntry
            {
                Id = p.Identity.EntryId, TenantId = request.TenantId, DocumentId = p.Identity.DocumentId,
                PostingId = p.Identity.PostingId, DocumentLineId = p.Identity.LineId, EntryNumber = 1,
                ItemId = p.Batch.ItemId, LocationId = request.LocationId, BatchId = p.Batch.Id, ZoneId = p.Batch.ZoneId,
                QuantityBefore = 0, QuantityDelta = p.Batch.Quantity, QuantityAfter = p.Batch.Quantity,
                BatchNumberSnapshot = p.Batch.BatchNumber, ExpiryDateSnapshot = p.Batch.ExpiryDate, OccurredAt = at, CreatedAt = at,
            }));
            await db.SaveChangesAsync(ct);
            foreach (var plan in plans)
            {
                var p = plan;
                var delta = new InventoryLedgerDeltaV1(p.Identity.EntryId, 1, p.Batch.ItemId, request.LocationId,
                    p.Batch.Id, p.Batch.Quantity, null, null, "UAH", p.Batch.ZoneId);
                var outbox = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(request.TenantId, p.Identity.MessageId,
                    new DateTimeOffset(at), new(p.Identity.DocumentId, p.Identity.DocumentKey, 1, p.Identity.PostingId, [delta]),
                    correlationId: request.RunId), historical: true);
                db.Add(outbox);
                db.Add(new InventoryBalanceProjection { TenantId = request.TenantId, ItemId = p.Batch.ItemId,
                    LocationId = request.LocationId, BatchId = p.Batch.Id, Quantity = p.Batch.Quantity,
                    ReservedQuantity = 0, LastLedgerEntryId = p.Identity.EntryId, UpdatedAt = at });
                db.Add(new InventoryReconciliation { TenantId = request.TenantId, RunId = request.RunId,
                    ItemId = p.Batch.ItemId, LocationId = request.LocationId, BatchId = p.Batch.Id,
                    LegacyQuantity = p.Batch.Quantity, LedgerQuantity = p.Batch.Quantity, Status = "matched", AsOf = at, CreatedAt = at,
                    Explanation = receipt });
            }
            await db.SaveChangesAsync(ct);
            foreach (var document in documents) { document.Status = "posted"; document.PostedAt = at; }
            await db.SaveChangesAsync(ct);
            // Read back the actual persisted delta/projection before committing the atomic graph.
            var sums = await db.InventoryLedgerEntries.AsNoTracking().Where(x => x.TenantId == request.TenantId && selected.Contains(x.BatchId))
                .GroupBy(x => x.BatchId).Select(g => new { BatchId = g.Key, Quantity = g.Sum(x => x.QuantityDelta) }).ToDictionaryAsync(x => x.BatchId, x => x.Quantity, ct);
            var balances = await db.InventoryBalanceProjections.AsNoTracking().Where(x => x.TenantId == request.TenantId && selected.Contains(x.BatchId))
                .ToDictionaryAsync(x => x.BatchId, ct);
            if (plans.Any(p => sums.GetValueOrDefault(p.Batch.Id) != p.Batch.Quantity
                || !balances.TryGetValue(p.Batch.Id, out var b) || b.Quantity != p.Batch.Quantity || b.ReservedQuantity != 0
                || b.LastLedgerEntryId != p.Identity.EntryId))
                throw new MaterializationConflictException("Persisted ledger/projection differs from the captured control quantity.");
            var result = new ControlBalanceResult(request.TenantId, request.LocationId, request.RunId, requestHash,
                request.SourceFingerprint, at, false, plans.Select(p => Row(p.Batch.Id, p.Identity, p.Batch.Quantity)).ToArray());
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new MaterializationConflictException("Concurrent activity prevented maintenance materialization; retry the same request after reviewing source state.");
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<ControlBalanceResult> ReplayAsync(ControlBalanceRequest request, string hash,
        IReadOnlyList<InventoryReconciliation> prior, CancellationToken ct)
    {
        if (prior.Count != request.BatchIds.Count || prior.Any(x => x.LocationId != request.LocationId || !request.BatchIds.Contains(x.BatchId)
            || x.Status != "matched" || x.LegacyQuantity is null || x.LegacyQuantity != x.LedgerQuantity || x.AsOf != prior[0].AsOf))
            throw new MaterializationConflictException("Run ID already belongs to a different or incomplete request.");
        var rows = new List<ControlBalanceRow>();
        foreach (var row in prior.OrderBy(x => x.BatchId.ToString("N"), StringComparer.Ordinal))
        {
            Receipt? receipt;
            try { receipt = JsonSerializer.Deserialize<Receipt>(row.Explanation ?? ""); }
            catch (JsonException) { throw new MaterializationConflictException("Run ID is not a materialization receipt."); }
            if (receipt?.Operation != ControlBalancePlanner.Version || receipt.RequestHash != hash
                || receipt.SourceFingerprint != request.SourceFingerprint)
                throw new MaterializationConflictException("Run ID was used with different parameters.");
            var identity = InventoryDryRunAnalyzer.Identity(request.TenantId, "legacy.control_balance", row.BatchId);
            if (!await db.DocumentPostings.AsNoTracking().AnyAsync(p => p.TenantId == request.TenantId && p.Id == identity.PostingId
                && p.DocumentId == identity.DocumentId && p.RequestHash == hash, ct)
                || !await db.InventoryLedgerEntries.AsNoTracking().AnyAsync(e => e.TenantId == request.TenantId && e.Id == identity.EntryId
                    && e.PostingId == identity.PostingId && e.BatchId == row.BatchId && e.QuantityDelta == row.LegacyQuantity, ct)
                || !await db.OutboxEvents.AsNoTracking().AnyAsync(o => o.TenantId == request.TenantId && o.Id == identity.MessageId
                    && o.PostingId == identity.PostingId && o.DeliveryScope == "historical" && o.Status == "suppressed", ct))
                throw new MaterializationConflictException("Committed materialization receipt has inconsistent linked history.");
            rows.Add(Row(row.BatchId, identity, row.LegacyQuantity!.Value));
        }
        // Replay acknowledges the original commit, not the current reconciliation after later legacy activity.
        return new(request.TenantId, request.LocationId, request.RunId, hash, request.SourceFingerprint, prior[0].AsOf, true, rows);
    }

    private static ControlBalanceRow Row(Guid batch, ProposedIdentity id, decimal quantity) =>
        new(batch, id.DocumentId, id.PostingId, id.EntryId, id.MessageId, quantity);
    private sealed record Receipt(string Operation, string RequestHash, string SourceFingerprint, string Reason, string AcceptanceReference);
}
