using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Domain.Exceptions;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed record PosInventoryPostingOptions(bool Enabled);

/// <summary>Stages ERP on the same connection/transaction as the existing POS unit of work.</summary>
public sealed class PosInventoryPostingBridge(AppDbContext db, IInventoryPostingGate gate,
    PosInventoryPostingOptions options, IWorkAccessContext access)
{
    public const string Source = "pos.sale.v1";
    public async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return false;
        var sales = db.ChangeTracker.Entries<PosTransaction>().Where(e => e.State == EntityState.Added).ToArray();
        if (sales.Length == 0) return false; // Fiscal retries/shift changes never post stock again.
        var sale = sales[0].Entity;
        var configured = options.Enabled && gate.Allows(sale.TenantId, sale.StoreId);
        var registry = await db.Database.SqlQuery<bool>($"SELECT to_regclass('public.inventory_posting_scopes') IS NOT NULL AS \"Value\"").SingleAsync(ct);
        var fenced = registry && await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE \"TenantId\"={sale.TenantId} AND \"LocationId\"={sale.StoreId}) AS \"Value\"").SingleAsync(ct);
        if (!configured && !fenced) return false;
        if (!configured || !fenced || sales.Length != 1 || db.Database.CurrentTransaction != null)
        {
            db.ChangeTracker.Clear();
            throw Conflict("ERP POS requires one sale, an enabled writer and an activated scope.");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(1)); ct = deadline.Token;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var scope = await access.GetAsync(ct) ?? throw Conflict("Live POS operator required.");
            await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true),set_config('app.erp_posting_id','',true),set_config('app.erp_review_document_id','',true)", ct);
            if (scope.TenantId != sale.TenantId || scope.UserId != sale.CashierId || !RegisterService.IsPosRole(scope.Role) || !scope.CanLocation(sale.StoreId))
                throw Conflict("Operator and sale location require verification. Cross-cashier ERP sync is unavailable.");
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == scope.UserId && u.TenantId == scope.TenantId && u.IsActive && u.Role == scope.Role, ct);
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == scope.TenantId && t.IsActive, ct);
            if (user == null || tenant == null || !tenant.HasModule("inventory") || !tenant.HasModule("pos")
                || !await db.Locations.AsNoTracking().AnyAsync(l => l.Id == sale.StoreId && l.TenantId == sale.TenantId && l.IsActive, ct)
                || scope.Role != "enterprise_admin" && !await db.UserLocations.AsNoTracking().AnyAsync(l => l.UserId == scope.UserId && l.TenantId == scope.TenantId && l.LocationId == sale.StoreId, ct)
                || user.Permissions?.GetValueOrDefault("pos", true) == false && !await db.UserPermissionGrants.AsNoTracking().AnyAsync(g => g.TenantId == scope.TenantId && g.UserId == scope.UserId && g.PermissionKey == "pos" && g.RevokedAt == null && g.ExpiresAt > DateTime.UtcNow, ct))
                throw Conflict("Live POS rights or modules changed.");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{scope.Role},true)", ct);
            if (!await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)
                || !await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({sale.TenantId},{sale.StoreId}) AS \"Value\"").SingleAsync(ct))
                throw Conflict("ERP POS migration required.");
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.product_stock IN EXCLUSIVE MODE; LOCK TABLE public.inventory_reservations IN SHARE MODE", ct);
            await using var stage = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options);
            await stage.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var shift = await stage.PosShifts.FromSqlInterpolated($"SELECT *,xmin FROM public.pos_shifts WHERE \"TenantId\"={sale.TenantId} AND \"Id\"={sale.ShiftId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (shift == null || shift.ClosedAt != null || shift.StoreId != sale.StoreId || shift.RegisterId != sale.CashRegisterId || shift.CashierId != scope.UserId)
                throw Conflict("The sale shift is no longer open at this register.");
            if (sale.CashRegisterId is Guid register && !await stage.CashRegisters.AsNoTracking().AnyAsync(r => r.Id == register && r.TenantId == sale.TenantId && r.StoreId == sale.StoreId && r.Status == CashRegister.StatusActive, ct))
                throw Conflict("The sale register is unavailable.");
            if (sale.ClientTransactionId is not Guid clientId || clientId == Guid.Empty || sale.Status != "pending_fiscalization" || sale.Items.Count is < 1 or > 100 || sale.Items.Any(i => !TransferPostingRules.Exact(i.Quantity) || i.Quantity == 0))
                throw Conflict("Exact positive POS quantities and a new sale are required.");
            if (await stage.PosTransactions.AsNoTracking().AnyAsync(t => t.TenantId == sale.TenantId && t.ClientTransactionId == clientId, ct))
                throw new DuplicateClientTransactionException("The concurrent sale already committed; resolve its original request.");
            var events = db.ChangeTracker.Entries<StockEvent>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray();
            var changed = db.ChangeTracker.Entries<ProductStock>().Where(e => e.State == EntityState.Modified && e.Property(s => s.Quantity).IsModified).ToArray();
            if (events.Length == 0 || changed.Length is < 1 or > 100 || events.Any(e => e.TenantId != sale.TenantId || e.StoreId != sale.StoreId || e.PerformedBy != scope.UserId || e.EventType != "pos_sale" || e.QuantityDelta is not < 0 || !e.ProductStockId.HasValue || !Matches(e.Meta, sale.Id)))
                throw Conflict("Every stock delta requires this sale's explicit event identity.");
            var occurredAt = sale.CreatedAt;
            var origins = events.Select(e => Origin(e.Meta)).ToArray();
            if (origins.Any(o => o.Offline))
            {
                var activated = await stage.Database.SqlQuery<DateTime>($"SELECT \"CreatedAt\" AS \"Value\" FROM public.inventory_posting_scopes WHERE \"TenantId\"={sale.TenantId} AND \"LocationId\"={sale.StoreId}").SingleAsync(ct);
                occurredAt = origins[0].At ?? throw Conflict("Offline sale time is missing.");
                if (sale.CashRegisterId == null || origins.Any(o => !o.Offline || !o.Frozen || o.At != occurredAt) || occurredAt < activated)
                    throw Conflict("Offline sales require verified original pricing and a post-activation sale time. Reconcile the old queue separately.");
            }
            var itemIds = sale.Items.Select(i => i.ProductId).Distinct().ToArray();
            if (await stage.InventoryReservations.AnyAsync(r => r.TenantId == sale.TenantId && r.LocationId == sale.StoreId && itemIds.Contains(r.ItemId) && r.Status == "active", ct))
                throw Conflict("Reserved inventory requires a separate POS policy.");
            // Verify the existing POS allocation against the complete current FEFO take, including phantoms.
            var candidates = await stage.ProductStocks.FromSqlInterpolated($"SELECT *,xmin FROM public.product_stock WHERE \"TenantId\"={sale.TenantId} AND \"LocationId\"={sale.StoreId} AND \"ProductId\"=ANY({itemIds}) AND \"Quantity\">0 ORDER BY \"Id\"").ToListAsync(ct);
            var expected = new Dictionary<Guid, decimal>(); var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var group in sale.Items.GroupBy(i => i.ProductId))
            {
                var remaining = group.Sum(i => i.Quantity);
                foreach (var batch in candidates.Where(b => b.ProductId == group.Key && b.ExpiryDate > today && b.Status is not ("archived" or "sold_out" or "expired" or "temp_violation")).OrderBy(b => b.ExpiryDate).ThenBy(b => b.AddedAt).ThenBy(b => b.Id))
                { var take = Math.Min(remaining, batch.Quantity); if (take > 0) expected[batch.Id] = take; remaining -= take; if (remaining == 0) break; }
                if (remaining != 0) throw Conflict("Insufficient current FEFO stock.");
            }
            var grouped = events.GroupBy(e => e.ProductStockId!.Value).ToDictionary(g => g.Key, g => g.ToArray());
            if (expected.Count != changed.Length || grouped.Count != expected.Count || expected.Any(p => !grouped.TryGetValue(p.Key, out var ev) || -ev.Sum(e => e.QuantityDelta!.Value) != p.Value))
                throw Conflict("POS stock events differ from the current FEFO allocation.");
            var rows = new List<(ProductStock Stock, InventoryBalanceProjection Balance, decimal Delta, decimal? Cost)>();
            foreach (var pending in changed.OrderBy(e => e.Entity.Id))
            {
                var stock = candidates.SingleOrDefault(b => b.Id == pending.Entity.Id) ?? throw Conflict("A consumed batch changed.");
                var delta = pending.Entity.Quantity - pending.Property(s => s.Quantity).OriginalValue;
                var balance = await stage.InventoryBalanceProjections.SingleOrDefaultAsync(b => b.TenantId == sale.TenantId && b.BatchId == stock.Id, ct);
                var ledger = stage.InventoryLedgerEntries.Where(e => e.TenantId == sale.TenantId && e.BatchId == stock.Id);
                if (stock.Quantity != pending.Property(s => s.Quantity).OriginalValue || stage.Entry(stock).Property<uint>("xmin").CurrentValue != pending.Property<uint>("xmin").OriginalValue
                    || delta != -expected.GetValueOrDefault(stock.Id) || !TransferPostingRules.Exact(pending.Entity.Quantity)
                    || grouped[stock.Id].Any(e => e.ProductId != stock.ProductId) || balance == null || balance.ItemId != stock.ProductId || balance.LocationId != stock.StoreId
                    || balance.Quantity != stock.Quantity || balance.ReservedQuantity != 0 || await ledger.SumAsync(e => e.QuantityDelta, ct) != stock.Quantity)
                    throw Conflict("POS stock, ledger and projection require fresh review.");
                var last = await ledger.SingleOrDefaultAsync(e => e.Id == balance.LastLedgerEntryId && e.QuantityAfter == stock.Quantity, ct) ?? throw Conflict("Missing ledger provenance.");
                rows.Add((stock, balance, delta, last.UnitCost));
            }
            // Persist only the source in the staging tracker; the caller's pending loyalty/shift/stock stay untouched.
            var sourceSale = (PosTransaction)db.Entry(sale).CurrentValues.ToObject();
            stage.Entry(sourceSale).State = EntityState.Added;
            foreach (var line in sale.Items) stage.Entry((PosTransactionItem)db.Entry(line).CurrentValues.ToObject()).State = EntityState.Added;
            await stage.SaveChangesAsync(ct);
            var snapshot = await stage.Database.SqlQuery<string>($"SELECT public.erp_w4_pos_snapshot({sale.Id})::text AS \"Value\"").SingleAsync(ct);
            using var json = JsonDocument.Parse(snapshot);
            var at = DateTime.UtcNow; at = new DateTime(at.Ticks - at.Ticks % 10, DateTimeKind.Utc);
            var hash = ReceiptPostingRules.Hash(new { source = json.RootElement, batches = rows.Select(r => new { r.Stock.Id, r.Delta, r.Cost }) });
            var doc = new BusinessDocument { TenantId = sale.TenantId, DocumentType = "pos_sale", SourceSystem = Source, SourceId = sale.Id.ToString(), Number = sale.ReceiptNumber,
                CreatedBy = scope.UserId, ContentHash = hash, OccurredAt = occurredAt, CreatedAt = at, UpdatedAt = at };
            stage.Add(doc); await stage.SaveChangesAsync(ct);
            var posting = new DocumentPosting { TenantId = sale.TenantId, DocumentId = doc.Id, IdempotencyKey = $"{Source}:{sale.Id:N}", RequestHash = hash, PerformedBy = scope.UserId, PostedAt = at };
            var entries = new List<InventoryLedgerEntry>();
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var sourceItem = sale.Items.First(l => l.ProductId == r.Stock.ProductId);
                var product = await stage.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={sale.TenantId} AND \"Id\"={r.Stock.ProductId} FOR SHARE").SingleAsync(ct);
                if (string.IsNullOrWhiteSpace(product.Unit) || product.Unit.Length > 24 || string.IsNullOrWhiteSpace(sourceItem.ProductNameSnapshot)) throw Conflict("POS item snapshot is missing.");
                foreach (var stockEvent in grouped[r.Stock.Id])
                { using var meta = JsonDocument.Parse(stockEvent.Meta!); if (!meta.RootElement.TryGetProperty("unit",out var unit) || unit.GetString()!=product.Unit) throw Conflict("The POS unit changed; reconcile the original receipt."); }
                var line = new DocumentLine { TenantId = sale.TenantId, DocumentId = doc.Id, LineNumber = i + 1, ItemId = r.Stock.ProductId, FromLocationId = sale.StoreId,
                    Quantity = -r.Delta, Unit = product.Unit, UnitCost = r.Cost, ItemNameSnapshot = sourceItem.ProductNameSnapshot, BarcodeSnapshot = sourceItem.BarcodeSnapshot,
                    BatchNumberSnapshot = r.Stock.BatchNumber, ExpiryDateSnapshot = r.Stock.ExpiryDate,
                    Metadata = JsonSerializer.Serialize(new { saleId = sale.Id, batchId = r.Stock.Id, source = json.RootElement, eventIds = grouped[r.Stock.Id].Select(e => e.Id).OrderBy(id => id) }) };
                stage.Add(line);
                entries.Add(new() { TenantId = sale.TenantId, DocumentId = doc.Id, DocumentLineId = line.Id, PostingId = posting.Id, EntryNumber = i + 1, ItemId = r.Stock.ProductId,
                    LocationId = sale.StoreId, BatchId = r.Stock.Id, ZoneId = r.Stock.ZoneId, QuantityBefore = r.Stock.Quantity, QuantityDelta = r.Delta, QuantityAfter = r.Stock.Quantity + r.Delta,
                    UnitCost = r.Cost, ValueDelta = r.Cost.HasValue ? decimal.Round(r.Delta * r.Cost.Value, 4, MidpointRounding.AwayFromZero) : null,
                    BatchNumberSnapshot = r.Stock.BatchNumber, ExpiryDateSnapshot = r.Stock.ExpiryDate, CreatedAt = at, OccurredAt = occurredAt });
            }
            await stage.SaveChangesAsync(ct); doc.Status = "submitted"; await stage.SaveChangesAsync(ct);
            stage.Add(new DocumentApproval { TenantId = sale.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = scope.UserId, Decision = "approved", ContentHash = hash, Reason = "POS cashier confirmed sale", CreatedAt = at });
            doc.Status = "approved"; await stage.SaveChangesAsync(ct);
            stage.Add(posting); await stage.SaveChangesAsync(ct);
            stage.AddRange(entries); await stage.SaveChangesAsync(ct);
            await stage.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{posting.Id.ToString()},true)", ct);
            db.Entry(sale).State = EntityState.Unchanged;
            foreach (var line in sale.Items) db.Entry(line).State = EntityState.Unchanged;
            await db.SaveChangesAsync(ct); // Existing stock/events/loyalty/promotions/shift remain in this same transaction.
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var e = entries[i]; r.Balance.Quantity = e.QuantityAfter!.Value; r.Balance.LastLedgerEntryId = e.Id; r.Balance.UpdatedAt = at;
                stage.Add(new StockMovement { TenantId = sale.TenantId, ProductStockId = r.Stock.Id, ProductId = r.Stock.ProductId, MovementType = "sale",
                    FromStoreId = sale.StoreId, FromZoneId = r.Stock.ZoneId, Quantity = -r.Delta, QuantityBefore = e.QuantityBefore, QuantityAfter = e.QuantityAfter,
                    ReferenceId = sale.Id, ReferenceType = "pos_sale", PerformedBy = scope.UserId, CreatedAt = at });
            }
            stage.Add(ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(sale.TenantId, Guid.NewGuid(), new(at),
                new(doc.Id, doc.DocumentKey, doc.Version, posting.Id, entries.Select(e => new InventoryLedgerDeltaV1(e.Id, e.EntryNumber, e.ItemId, e.LocationId, e.BatchId,
                    e.QuantityDelta, e.UnitCost, e.ValueDelta, e.Currency, e.ZoneId, e.ReversesEntryId)).ToArray()), correlationId: sale.Id)));
            await stage.SaveChangesAsync(ct); // Outbox and projection must exist before the W2 document is sealed.
            doc.Status = "posted"; doc.PostedAt = at; await stage.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return true;
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }
    private static ConcurrencyConflictException Conflict(string message) => new(message);
    private static bool Matches(string? meta, Guid id)
    { try { using var json = JsonDocument.Parse(meta ?? "{}"); return json.RootElement.TryGetProperty("tx_id", out var value) && value.GetGuid() == id; } catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return false; } }
    private static (bool Offline,bool Frozen,DateTime? At) Origin(string? meta)
    {
        using var json=JsonDocument.Parse(meta!);var r=json.RootElement;
        return (r.TryGetProperty("offline",out var o)&&o.ValueKind==JsonValueKind.True,
            r.TryGetProperty("frozen_offline",out var f)&&f.ValueKind==JsonValueKind.True,
            r.TryGetProperty("occurred_at",out var a)&&a.ValueKind==JsonValueKind.String&&a.TryGetDateTime(out var date)?date:null);
    }
}
