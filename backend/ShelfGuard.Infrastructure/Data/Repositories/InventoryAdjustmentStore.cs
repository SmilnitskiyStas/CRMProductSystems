using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>One explicit transaction owns the ERP graph and compatibility stock/movement writes.</summary>
public sealed class InventoryAdjustmentStore(AppDbContext db, IInventoryPostingGate gate, TimeProvider clock)
    : IInventoryAdjustmentStore
{
    public async Task<InventoryAdjustmentReceipt> PostAsync(InventoryAdjustmentPlan plan, CancellationToken ct = default)
    {
        if (!gate.Allows(plan.TenantId, plan.Request.LocationId)) throw new InventoryPostingDisabledException();
        var canonical = InventoryAdjustmentPlanner.Prepare(plan.TenantId, plan.ActorId, plan.Request);
        if (canonical != plan) throw new ArgumentException("Posting plan is not canonical.");
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Posting requires a clean PostgreSQL context and its own transaction.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        ct = deadline.Token;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'", ct);
            await EstablishScopeAsync(plan, ct);
            if (!await IntegratedScopeReadiness.Ready(db, plan.TenantId, plan.Request.LocationId, ct))
                throw new InventoryPostingConflictException("The inventory scope must be activated and fenced before posting.");
            // Serialize the tenant's request identity BEFORE reading the receipt, including requests targeting different batches.
            var requestLock = $"{plan.TenantId:N}:{plan.IdempotencyKey}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({requestLock},0))", ct);
            var prior = await db.DocumentPostings.AsNoTracking()
                .SingleOrDefaultAsync(p => p.TenantId == plan.TenantId && p.IdempotencyKey == plan.IdempotencyKey, ct);
            if (prior != null)
            {
                var replay = await ReplayAsync(plan, prior, ct);
                await transaction.CommitAsync(ct);
                return replay;
            }

            // Reservation commands are not enabled in W4-A. This short shared lock also excludes a non-cooperating insert
            // between the reserve check and commit; future reservation adapters will replace it with per-key coordination.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.inventory_reservations IN SHARE MODE", ct);
            var request = plan.Request;
            var batches = await db.ProductStocks.FromSqlInterpolated($"""
                SELECT product_stock.*, xmin FROM product_stock WHERE "TenantId"={plan.TenantId} AND "LocationId"={request.LocationId}
                  AND "Id"={request.BatchId} FOR UPDATE
                """).ToListAsync(ct);
            var stock = batches.SingleOrDefault() ?? throw new InventoryPostingNotFoundException();
            var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == stock.ProductId && i.TenantId == plan.TenantId, ct)
                ?? throw new InventoryPostingConflictException("Batch item is unavailable.");
            if (string.IsNullOrWhiteSpace(item.Unit) || string.IsNullOrWhiteSpace(item.Name))
                throw new InventoryPostingConflictException("The item must have an explicit name and unit.");
            var projections = await db.InventoryBalanceProjections.FromSqlInterpolated($"""
                SELECT inventory_balance_projections.*, xmin FROM inventory_balance_projections WHERE "TenantId"={plan.TenantId}
                  AND "LocationId"={request.LocationId} AND "ItemId"={stock.ProductId} AND "BatchId"={stock.Id} FOR UPDATE
                """).ToListAsync(ct);
            var projection = projections.SingleOrDefault();
            var ledger = db.InventoryLedgerEntries.AsNoTracking().Where(e => e.TenantId == plan.TenantId
                && e.ItemId == stock.ProductId && e.LocationId == stock.StoreId && e.BatchId == stock.Id);
            var hasLedger = await ledger.AnyAsync(ct);
            var ledgerQuantity = await ledger.Select(e => (decimal?)e.QuantityDelta).SumAsync(ct) ?? 0;
            if (stock.Quantity != ledgerQuantity
                || projection == null && (stock.Quantity != 0 || hasLedger)
                || projection != null && (projection.Quantity != stock.Quantity || projection.ReservedQuantity != 0
                    || projection.LastLedgerEntryId == null && hasLedger))
                throw new InventoryPostingConflictException("Stock, ledger and projection require reconciliation before posting.");
            if (projection?.LastLedgerEntryId is Guid last && !await ledger.AnyAsync(e => e.Id == last && e.QuantityAfter == stock.Quantity, ct))
                throw new InventoryPostingConflictException("Projection provenance is inconsistent with the batch.");
            if (await db.InventoryReservations.AsNoTracking().AnyAsync(r => r.TenantId == plan.TenantId
                && r.LocationId == stock.StoreId && r.ItemId == stock.ProductId && r.Status == "active"
                && (r.BatchId == null || r.BatchId == stock.Id), ct))
                throw new InventoryPostingConflictException("Active reservations are not supported by adjustment posting yet.");
            if (stock.Quantity != request.ExpectedQuantity)
                throw new InventoryPostingConflictException("Stock quantity changed; review the current count before retrying.");

            var now = clock.GetUtcNow().UtcDateTime;
            var at = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc); // PostgreSQL microsecond precision.
            var snapshot = new InventoryAdjustmentSnapshot(item.Id, item.Unit, item.Name, stock.BatchNumber,
                stock.ExpiryDate, stock.ZoneId, at);
            var document = new BusinessDocument { TenantId = plan.TenantId, DocumentType = "stock_count_adjustment",
                SourceSystem = InventoryAdjustmentPlanner.Version, SourceId = request.RequestId.ToString("N"),
                CreatedBy = plan.ActorId, ContentHash = InventoryAdjustmentPlanner.ContentHash(plan, snapshot),
                OccurredAt = at, CreatedAt = at, UpdatedAt = at };
            var delta = request.Quantity - stock.Quantity;
            var line = new DocumentLine { TenantId = plan.TenantId, DocumentId = document.Id, LineNumber = 1,
                ItemId = item.Id, FromLocationId = delta < 0 ? stock.StoreId : null,
                ToLocationId = delta > 0 ? stock.StoreId : null, Quantity = Math.Abs(delta), Unit = item.Unit,
                ItemNameSnapshot = item.Name, BatchNumberSnapshot = stock.BatchNumber, ExpiryDateSnapshot = stock.ExpiryDate,
                Metadata = JsonSerializer.Serialize(new { protocol = InventoryAdjustmentPlanner.Version,
                    request.RequestId, request.Reason, request.ExpectedQuantity, request.Quantity }) };
            var posting = new DocumentPosting { TenantId = plan.TenantId, DocumentId = document.Id,
                IdempotencyKey = plan.IdempotencyKey, RequestHash = plan.RequestHash, PerformedBy = plan.ActorId, PostedAt = at };
            var entry = new InventoryLedgerEntry { TenantId = plan.TenantId, DocumentId = document.Id,
                DocumentLineId = line.Id, PostingId = posting.Id, EntryNumber = 1, ItemId = item.Id,
                LocationId = stock.StoreId, BatchId = stock.Id, ZoneId = stock.ZoneId,
                QuantityBefore = stock.Quantity, QuantityDelta = delta, QuantityAfter = request.Quantity,
                BatchNumberSnapshot = stock.BatchNumber, ExpiryDateSnapshot = stock.ExpiryDate, OccurredAt = at, CreatedAt = at };
            var message = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(plan.TenantId, Guid.NewGuid(),
                new DateTimeOffset(at), new(document.Id, document.DocumentKey, document.Version, posting.Id,
                    [new(entry.Id, 1, item.Id, stock.StoreId, stock.Id, delta, null, null, document.Currency, stock.ZoneId)]),
                correlationId: request.RequestId));

            db.Add(document); await db.SaveChangesAsync(ct);
            db.Add(line); await db.SaveChangesAsync(ct);
            document.Status = "submitted"; await db.SaveChangesAsync(ct);
            // W4-A preserves the existing manager-only manual-adjustment floor; no second-person approval is implied.
            db.Add(new DocumentApproval { TenantId = plan.TenantId, DocumentId = document.Id, Stage = 1,
                ApproverId = plan.ActorId, Decision = "approved", ContentHash = document.ContentHash!,
                Reason = request.Reason, CreatedAt = at });
            document.Status = "approved"; await db.SaveChangesAsync(ct);
            db.Add(posting); await db.SaveChangesAsync(ct);
            db.Add(entry); await db.SaveChangesAsync(ct);
            // The DB fence verifies this staged posting and its exact ledger delta; the marker alone grants nothing.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{posting.Id.ToString()},true)", ct);
            if (projection == null)
            {
                projection = new InventoryBalanceProjection { TenantId = plan.TenantId, ItemId = item.Id,
                    LocationId = stock.StoreId, BatchId = stock.Id };
                db.Add(projection);
            }
            projection.Quantity = request.Quantity;
            projection.LastLedgerEntryId = entry.Id;
            projection.UpdatedAt = at;
            db.Add(new StockMovement { TenantId = plan.TenantId, ProductStockId = stock.Id, ProductId = item.Id,
                MovementType = "adjustment", FromStoreId = line.FromLocationId, ToStoreId = line.ToLocationId,
                FromZoneId = delta < 0 ? stock.ZoneId : null, ToZoneId = delta > 0 ? stock.ZoneId : null,
                Quantity = Math.Abs(delta), QuantityBefore = stock.Quantity, QuantityAfter = request.Quantity,
                ReferenceType = "stock_adjustment", ReferenceId = document.Id, PerformedBy = plan.ActorId, Notes = request.Reason });
            stock.Quantity = request.Quantity;
            stock.Status = StockStatus.Recalculate(stock);
            db.Add(message);
            // W2 append guards require approved while inserting the outbox. Finalize only after every row is staged.
            await db.SaveChangesAsync(ct);
            document.Status = "posted"; document.PostedAt = at;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct); // Deferred W2 constraints validate the complete graph here.
            return Receipt(plan, posting, entry, message, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InventoryPostingConflictException("A concurrent inventory update prevented posting.");
        }
        // Npgsql's non-retrying execution strategy can wrap transient SELECT failures in InvalidOperationException.
        catch (Exception error) when (error is PostgresException || error.InnerException is PostgresException)
        {
            var pg = error as PostgresException ?? (PostgresException)error.InnerException!;
            if (pg.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.SerializationFailure
                or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation
                || pg.SqlState == PostgresErrorCodes.QueryCanceled && !ct.IsCancellationRequested)
                throw new InventoryPostingConflictException("Concurrent activity or a database timeout prevented posting; retry the same request identity.");
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task EstablishScopeAsync(InventoryAdjustmentPlan plan, CancellationToken ct)
    {
        // Explicitly remove inherited worker/provider/consumer bypass, even if a caller supplied the connection.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{plan.TenantId.ToString()},true), set_config('app.user_id',{plan.ActorId.ToString()},true), set_config('app.role','',true), set_config('app.consumer_account_id','',true), set_config('app.erp_posting_id','',true)", ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == plan.ActorId && u.TenantId == plan.TenantId && u.IsActive, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == plan.TenantId && t.IsActive, ct);
        if (user == null || tenant == null || !tenant.HasModule("inventory") || !InventoryAdjustmentPlanner.CanAdjust(user.Role))
            throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{user.Role},true)", ct);
        if (!await db.Locations.AsNoTracking().AnyAsync(l => l.Id == plan.Request.LocationId && l.TenantId == plan.TenantId && l.IsActive, ct)
            || user.Role != "enterprise_admin" && !await db.UserLocations.AsNoTracking().AnyAsync(l => l.TenantId == plan.TenantId
                && l.UserId == plan.ActorId && l.LocationId == plan.Request.LocationId, ct))
            throw new WorkAccessDeniedException();
    }

    private async Task<InventoryAdjustmentReceipt> ReplayAsync(InventoryAdjustmentPlan plan, DocumentPosting posting, CancellationToken ct)
    {
        if (posting.RequestHash != plan.RequestHash || posting.PerformedBy != plan.ActorId)
            throw new InventoryPostingConflictException("Request identity was already used with different parameters.");
        var document = await db.BusinessDocuments.AsNoTracking().SingleAsync(d => d.TenantId == plan.TenantId && d.Id == posting.DocumentId, ct);
        var entries = await db.InventoryLedgerEntries.AsNoTracking().Where(e => e.TenantId == plan.TenantId && e.PostingId == posting.Id).ToListAsync(ct);
        var messages = await db.OutboxEvents.AsNoTracking().Where(e => e.TenantId == plan.TenantId && e.PostingId == posting.Id).ToListAsync(ct);
        var lines = await db.DocumentLines.AsNoTracking().Where(l => l.TenantId == plan.TenantId && l.DocumentId == document.Id).ToListAsync(ct);
        if (document.Status != "posted" || document.DocumentType != "stock_count_adjustment"
            || document.SourceSystem != InventoryAdjustmentPlanner.Version || document.SourceId != plan.Request.RequestId.ToString("N")
            || entries.Count != 1 || messages.Count != 1 || lines.Count != 1)
            throw new InventoryPostingConflictException("The original posting receipt is incomplete or no longer active.");
        var entry = entries[0]; var line = lines[0]; var message = messages[0];
        var snapshot = new InventoryAdjustmentSnapshot(entry.ItemId, line.Unit, line.ItemNameSnapshot,
            entry.BatchNumberSnapshot, entry.ExpiryDateSnapshot!.Value, entry.ZoneId, entry.OccurredAt);
        if (entry.BatchId != plan.Request.BatchId || entry.LocationId != plan.Request.LocationId
            || entry.QuantityBefore != plan.Request.ExpectedQuantity || entry.QuantityAfter != plan.Request.Quantity
            || entry.QuantityDelta != plan.Request.Quantity - plan.Request.ExpectedQuantity
            || entry.DocumentId != document.Id || entry.DocumentLineId != line.Id || line.ItemId != entry.ItemId
            || document.ContentHash != InventoryAdjustmentPlanner.ContentHash(plan, snapshot)
            || message.DeliveryScope != "live" || message.CorrelationId != plan.Request.RequestId)
            throw new InventoryPostingConflictException("The original posting receipt differs from the request.");
        ErpEventContracts.FromOutbox(message);
        return Receipt(plan, posting, entry, message, true);
    }

    private static InventoryAdjustmentReceipt Receipt(InventoryAdjustmentPlan plan, DocumentPosting posting,
        InventoryLedgerEntry entry, OutboxEvent message, bool replayed) => new(plan.TenantId, plan.Request.RequestId,
            posting.DocumentId, posting.Id, entry.Id, message.Id, entry.BatchId,
            entry.QuantityBefore!.Value, entry.QuantityAfter!.Value, posting.PostedAt, replayed);
}
