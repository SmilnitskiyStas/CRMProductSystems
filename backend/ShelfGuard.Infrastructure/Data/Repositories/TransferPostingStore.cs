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

public sealed class TransferPostingStore(DbContextOptions<AppDbContext> options, IInventoryPostingGate gate,
    TimeProvider clock, TransferPostingOptions transfers) : ITransferPostingStore
{
    private sealed record Row(StockTransferItem Item, ProductStock Stock, InventoryBalanceProjection? Balance,
        string Name, string Unit, string? Barcode, decimal? Cost, InventoryLedgerEntry? Original = null);
    private AppDbContext Context() => new(options);
    public async Task<TransferPostingPreview> PreviewDispatchAsync(WorkAccessScope scope, TransferDispatchCommand command, CancellationToken ct)
    {
        command = TransferPostingRules.Normalize(command);
        await using var db = Context(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var result=await PreviewDispatchInContextAsync(db,scope,command,ct);await tx.CommitAsync(ct);return result;
    }
    internal async Task<TransferPostingPreview> PreviewDispatchInContextAsync(AppDbContext db,WorkAccessScope scope,TransferDispatchCommand command,CancellationToken ct)
    {
        await Establish(db, scope, "dispatch", ct); await Locations(db, scope, command.FromLocationId, command.ToLocationId, command.FromLocationId, ct);
        var rows = await DispatchRows(db, scope, command, false, ct);
        var ready = await Ready(db, scope.TenantId, command.FromLocationId, command.ToLocationId, ct);
        var configured = Configured(scope.TenantId, command.FromLocationId, command.ToLocationId);
        var fp = DispatchFingerprint(scope.TenantId, command, rows);
        return new(command.RequestId, "dispatch", fp, configured, ready, configured && ready, Review(rows, true));
    }
    public async Task<TransferPostingPreview> PreviewAsync(WorkAccessScope scope, Guid transferId, string phase, CancellationToken ct)
    {
        TransferPostingRules.Phase(phase);
        await using var db = Context(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        await Establish(db, scope, phase, ct);
        var t = await Header(db, scope, transferId, false, ct);
        await Locations(db, scope, t.FromStoreId, t.ToStoreId, phase == "receive" ? t.ToStoreId : t.FromStoreId, ct);
        var (original, rows) = await TerminalRows(db, scope, t, phase, false, ct);
        var ready = await Ready(db, scope.TenantId, t.FromStoreId, t.ToStoreId, ct);
        var configured = Configured(scope.TenantId, t.FromStoreId, t.ToStoreId);
        var eligible = t.Status == "in_transit" || await db.DocumentPostings.AnyAsync(p => p.TenantId == scope.TenantId && p.IdempotencyKey == TransferPostingRules.Key(t.Id, phase), ct);
        var fp = TerminalFingerprint(t, phase, original, rows);
        await tx.CommitAsync(ct); return new(t.Id, phase, fp, configured, ready, configured && ready && eligible, Review(rows, phase == "cancel"));
    }
    public Task<TransferPostingResult> DispatchAsync(WorkAccessScope scope, TransferDispatchCommand command, CancellationToken ct) =>
        Post(scope, TransferPostingRules.Normalize(command), Guid.Empty, "dispatch", new(command.ExpectedFingerprint!), ct);
    public Task<TransferPostingResult> CompleteAsync(WorkAccessScope scope, Guid id, string phase, TransferActionCommand command, CancellationToken ct)
    {
        TransferPostingRules.Phase(phase);
        return Post(scope, null, id, phase, command with { Reason = TransferPostingRules.Reason(phase, command.Reason) }, ct);
    }
    private async Task<TransferPostingResult> Post(WorkAccessScope scope, TransferDispatchCommand? command, Guid id,
        string phase, TransferActionCommand action, CancellationToken ct)
    {
        TransferPostingRules.Fingerprint(action.ExpectedFingerprint);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(1)); ct = deadline.Token;
        await using var db = Context(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try{var result=await PostInContextAsync(db,scope,command,id,phase,action,ct);await tx.CommitAsync(ct);return result;}finally{db.ChangeTracker.Clear();}
    }
    internal async Task<TransferPostingResult> PostInContextAsync(AppDbContext db,WorkAccessScope scope,TransferDispatchCommand? command,Guid id,string phase,TransferActionCommand action,CancellationToken ct)
    {
        try
        {
            await Establish(db, scope, phase, ct);
            if (command != null) id = command.RequestId;
            // Serializes creation of a natural transfer ID, including concurrent exact retry before its header exists.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope.TenantId.ToString() + ':' + id.ToString()},0))", ct);
            StockTransfer t;
            if (command != null)
            {
                await Locations(db, scope, command.FromLocationId, command.ToLocationId, command.FromLocationId, ct);
                t = new() { Id = id, TenantId = scope.TenantId, FromStoreId = command.FromLocationId, ToStoreId = command.ToLocationId,
                    TransferType = command.TransferType, Notes = command.Notes, InitiatedBy = scope.UserId, Status = "in_transit" };
            }
            else
            {
                t = await Header(db, scope, id, true, ct);
                await Locations(db, scope, t.FromStoreId, t.ToStoreId, phase == "receive" ? t.ToStoreId : t.FromStoreId, ct);
            }
            if (!Configured(scope.TenantId, t.FromStoreId, t.ToStoreId)) throw new InventoryPostingDisabledException();
            if (!await Ready(db, scope.TenantId, t.FromStoreId, t.ToStoreId, ct)) throw new InventoryPostingConflictException("Both transfer scopes require the transfer fence upgrade.");
            var requestHash = ReceiptPostingRules.Hash(new { protocol = TransferPostingRules.Source(phase), action.ExpectedFingerprint, action.Reason,
                command = command == null ? null : DispatchIdentity(command) });
            var prior = await db.DocumentPostings.SingleOrDefaultAsync(p => p.TenantId == scope.TenantId && p.IdempotencyKey == TransferPostingRules.Key(id, phase), ct);
            if (prior != null)
            {
                if (prior.RequestHash != requestHash) throw new InventoryPostingConflictException("Transfer ID belongs to a different confirmation.");
                var d = await db.BusinessDocuments.SingleAsync(x => x.TenantId == scope.TenantId && x.Id == prior.DocumentId, ct);
                var evt = await db.OutboxEvents.SingleAsync(x => x.TenantId == scope.TenantId && x.PostingId == prior.Id, ct);
                var live = await Header(db, scope, id, false, ct);
                if (!(d.Status == "posted" || phase == "dispatch" && d.Status == "reversed") || d.SourceSystem != TransferPostingRules.Source(phase) || d.SourceId != id.ToString()
                    || phase != "dispatch" && live.Status != (phase == "receive" ? "received" : "cancelled") || evt.DeliveryScope != "live")
                    throw new InventoryPostingConflictException("Original transfer graph is inconsistent.");
                ErpEventContracts.FromOutbox(evt); return new(id, phase, d.Id, prior.Id, evt.Id, true);
            }
            BusinessDocument? original = null; List<Row> rows;
            if (command != null)
            {
                if (await db.StockTransfers.AnyAsync(x => x.Id == id, ct)) throw new InventoryPostingConflictException("Transfer identity is already used.");
                rows = await DispatchRows(db, scope, command, true, ct);
                if (DispatchFingerprint(scope.TenantId, command, rows) != action.ExpectedFingerprint) throw new InventoryPostingConflictException("Reviewed source stock changed.");
                foreach (var row in rows) t.Items.Add(row.Item);
                db.Add(t); await db.SaveChangesAsync(ct);
            }
            else
            {
                if (t.Status != "in_transit") throw new InventoryPostingConflictException("Transfer is already terminal.");
                (original, rows) = await TerminalRows(db, scope, t, phase, true, ct);
                if (TerminalFingerprint(t, phase, original, rows) != action.ExpectedFingerprint) throw new InventoryPostingConflictException("Transfer changed since review.");
            }
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.inventory_reservations IN SHARE MODE", ct);
            var itemIds = rows.Select(x => x.Item.ProductId).Distinct().ToArray();
            if (await db.InventoryReservations.AnyAsync(x => x.TenantId == scope.TenantId && itemIds.Contains(x.ItemId)
                && (x.LocationId == t.FromStoreId || x.LocationId == t.ToStoreId) && x.Status == "active", ct))
                throw new InventoryPostingConflictException("Active transfer-item reservations are not supported yet.");
            var time = clock.GetUtcNow().UtcDateTime; var at = new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Utc);
            var doc = new BusinessDocument { TenantId = scope.TenantId, DocumentType = "stock_transfer", SourceSystem = TransferPostingRules.Source(phase),
                SourceId = id.ToString(), CreatedBy = scope.UserId, CreatedAt = at, UpdatedAt = at, OccurredAt = at,
                ReversesDocumentId = phase == "cancel" ? original!.Id : null,
                ContentHash = ReceiptPostingRules.Hash(new { requestHash, actor = scope.UserId, at }) };
            var posting = new DocumentPosting { TenantId = scope.TenantId, DocumentId = doc.Id, PerformedBy = scope.UserId,
                PostedAt = at, IdempotencyKey = TransferPostingRules.Key(id, phase), RequestHash = requestHash,
                ReversesPostingId = phase == "cancel" ? rows[0].Original!.PostingId : null };
            db.Add(doc); await db.SaveChangesAsync(ct);
            var entries = new List<InventoryLedgerEntry>(); var stocks = new List<ProductStock>(); var balances = new List<InventoryBalanceProjection>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; var qty = row.Item.Quantity;
                var stock = phase == "receive" ? new ProductStock { TenantId = scope.TenantId, StoreId = t.ToStoreId, ProductId = row.Item.ProductId,
                    Quantity = qty, QuantityInitial = qty, BatchNumber = row.Item.BatchNumber, ExpiryDate = row.Item.ExpiryDate,
                    SourceType = "transfer", SourceId = id, AddedBy = scope.UserId, AddedAt = at, LastCheckedAt = at,
                    Status = row.Stock.Status == StockStatus.TemperatureHold ? StockStatus.TemperatureHold : StockStatus.Compute(qty, row.Item.ExpiryDate, at) } : row.Stock;
                var before = phase == "receive" ? 0 : stock.Quantity; var delta = phase == "dispatch" ? -qty : qty; var after = before + delta;
                if (!TransferPostingRules.Exact(after)) throw new InventoryPostingConflictException("Stock would overflow or become negative.");
                var value = row.Cost.HasValue ? decimal.Round(delta * row.Cost.Value, 4, MidpointRounding.AwayFromZero) : (decimal?)null;
                if (value.HasValue && decimal.Abs(value.Value) > 9_999_999_999.99m) throw new ArgumentException("Transfer value exceeds compatibility money precision.");
                var line = new DocumentLine { TenantId = scope.TenantId, DocumentId = doc.Id, LineNumber = i + 1, ItemId = row.Item.ProductId,
                    FromLocationId = t.FromStoreId, ToLocationId = t.ToStoreId, Quantity = qty, Unit = row.Unit, UnitCost = row.Cost,
                    ItemNameSnapshot = row.Name, BarcodeSnapshot = row.Barcode, BatchNumberSnapshot = row.Item.BatchNumber, ExpiryDateSnapshot = row.Item.ExpiryDate,
                    Metadata = JsonSerializer.Serialize(new { transferId = id, transferItemId = row.Item.Id, sourceBatchId = row.Item.ProductStockId,
                        batchId = stock.Id, originalEntryId = row.Original?.Id }) };
                db.Add(line); stocks.Add(stock);
                entries.Add(new() { TenantId = scope.TenantId, DocumentId = doc.Id, DocumentLineId = line.Id, PostingId = posting.Id,
                    EntryNumber = i + 1, BatchId = stock.Id, ItemId = stock.ProductId, LocationId = stock.StoreId, ZoneId = stock.ZoneId,
                    QuantityBefore = before, QuantityDelta = delta, QuantityAfter = after, UnitCost = row.Cost, ValueDelta = value,
                    BatchNumberSnapshot = row.Item.BatchNumber, ExpiryDateSnapshot = row.Item.ExpiryDate, OccurredAt = at, CreatedAt = at,
                    ReversesEntryId = phase == "cancel" ? row.Original!.Id : null });
                balances.Add(phase == "receive" ? new() { TenantId = scope.TenantId, BatchId = stock.Id, ItemId = stock.ProductId, LocationId = stock.StoreId } : row.Balance!);
            }
            await db.SaveChangesAsync(ct); doc.Status = "submitted"; await db.SaveChangesAsync(ct);
            db.Add(new DocumentApproval { TenantId = scope.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = scope.UserId, Decision = "approved",
                ContentHash = doc.ContentHash!, Reason = action.Reason ?? $"Operator confirmed reviewed transfer {phase}", CreatedAt = at });
            doc.Status = "approved"; await db.SaveChangesAsync(ct); db.Add(posting); await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{posting.Id.ToString()},true)", ct);
            if (phase == "receive") { db.AddRange(stocks); await db.SaveChangesAsync(ct); }
            db.AddRange(entries); await db.SaveChangesAsync(ct);
            for (var i = 0; i < rows.Count; i++)
            {
                var stock = stocks[i]; var entry = entries[i]; var balance = balances[i];
                if (phase != "receive") { stock.Quantity = entry.QuantityAfter!.Value; stock.Status = StockStatus.Recalculate(stock); }
                balance.Quantity = entry.QuantityAfter!.Value; balance.ReservedQuantity = 0; balance.LastLedgerEntryId = entry.Id; balance.UpdatedAt = at;
                if (phase == "receive") db.Add(balance);
                db.Add(new StockMovement { TenantId = scope.TenantId, ProductStockId = stock.Id, ProductId = stock.ProductId, MovementType = "transfer",
                    FromStoreId = phase == "cancel" ? null : t.FromStoreId, ToStoreId = phase == "cancel" ? t.FromStoreId : t.ToStoreId,
                    ToZoneId = phase == "cancel" ? stock.ZoneId : null, Quantity = rows[i].Item.Quantity,
                    QuantityBefore = entry.QuantityBefore, QuantityAfter = entry.QuantityAfter, PerformedBy = scope.UserId,
                    UnitPrice = entry.UnitCost, TotalAmount = entry.ValueDelta.HasValue ? decimal.Round(decimal.Abs(entry.ValueDelta.Value), 2, MidpointRounding.AwayFromZero) : null,
                    ReferenceType = phase == "cancel" ? "stock_transfer_cancellation" : "stock_transfer", ReferenceId = id });
            }
            if (original != null) db.Add(new DocumentLink { TenantId = scope.TenantId, FromDocumentId = original.Id, ToDocumentId = doc.Id,
                RelationType = phase == "cancel" ? "cancels" : "receives", CreatedAt = at });
            var message = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(scope.TenantId, Guid.NewGuid(), new(at),
                new(doc.Id, doc.DocumentKey, doc.Version, posting.Id, entries.Select(e => new InventoryLedgerDeltaV1(e.Id, e.EntryNumber,
                    e.ItemId, e.LocationId, e.BatchId, e.QuantityDelta, e.UnitCost, e.ValueDelta, e.Currency, e.ZoneId, e.ReversesEntryId)).ToArray(), posting.ReversesPostingId), correlationId: id));
            db.Add(message);
            if (phase != "dispatch") { t.Status = phase == "receive" ? "received" : "cancelled"; if (phase == "receive") t.ConfirmedBy = scope.UserId; }
            if(phase!="dispatch")await RequestShipmentLifecycle.ApplyAsync(db,scope,t,phase,ct);
            await db.SaveChangesAsync(ct); doc.Status = "posted"; doc.PostedAt = at;
            if (phase == "cancel") { original!.Status = "reversed"; original.UpdatedAt = at; }
            await db.SaveChangesAsync(ct);
            return new(id, phase, doc.Id, posting.Id, message.Id, false);
        }
        catch (DbUpdateConcurrencyException) { throw new InventoryPostingConflictException("Concurrent transfer change."); }
        catch (Exception e) when (e is PostgresException || e.InnerException is PostgresException)
        {
            var pg = e as PostgresException ?? (PostgresException)e.InnerException!;
            if (pg.SqlState is "55P03" or "40001" or "40P01" or "23505" || pg.SqlState == "57014" && !ct.IsCancellationRequested)
                throw new InventoryPostingConflictException("Concurrent transfer activity; retry the original command.");
            throw;
        }
    }
    private bool Configured(Guid tenant, Guid from, Guid to) => transfers.Enabled && gate.Allows(tenant, from) && gate.Allows(tenant, to);
    private static async Task Establish(AppDbContext db, WorkAccessScope scope, string phase, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true),set_config('app.erp_posting_id','',true)", ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == scope.UserId && u.TenantId == scope.TenantId && u.IsActive, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == scope.TenantId && t.IsActive, ct);
        if (user == null || user.Role != scope.Role || !ReceiptPostingRules.CanReceive(user.Role) || phase == "cancel" && !InventoryAdjustmentPlanner.CanAdjust(user.Role)
            || tenant == null || !tenant.HasModule("inventory")) throw new WorkAccessDeniedException();
        if (user.Permissions?.GetValueOrDefault("transfers", true) == false && !await db.UserPermissionGrants.AnyAsync(g => g.TenantId == scope.TenantId
            && g.UserId == scope.UserId && g.PermissionKey == "transfers" && g.RevokedAt == null && g.ExpiresAt > DateTime.UtcNow, ct)) throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{user.Role},true)", ct);
    }
    private static async Task Locations(AppDbContext db, WorkAccessScope scope, Guid from, Guid to, Guid acting, CancellationToken ct)
    {
        if (from == to || await db.Locations.CountAsync(l => l.TenantId == scope.TenantId && l.IsActive && (l.Id == from || l.Id == to), ct) != 2
            || !scope.CanLocation(acting) || scope.Role != "enterprise_admin" && !await db.UserLocations.AnyAsync(l => l.TenantId == scope.TenantId
                && l.UserId == scope.UserId && l.LocationId == acting, ct)) throw new WorkAccessDeniedException();
    }
    private static async Task<bool> Ready(AppDbContext db, Guid tenant, Guid from, Guid to, CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)) return false;
        return await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{from}) AND public.erp_w4_integrated_ready({tenant},{to}) AS \"Value\"").SingleAsync(ct);
    }
    private static async Task<StockTransfer> Header(AppDbContext db, WorkAccessScope scope, Guid id, bool locked, CancellationToken ct)
    {
        var query = locked ? db.StockTransfers.FromSqlInterpolated($"SELECT stock_transfers.*,xmin FROM public.stock_transfers WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={id} FOR UPDATE")
            : db.StockTransfers.Where(t => t.TenantId == scope.TenantId && t.Id == id);
        return (await query.ToListAsync(ct)).SingleOrDefault() ?? throw new InventoryPostingNotFoundException();
    }
    private static async Task<(ProductStock Stock, InventoryBalanceProjection Balance, decimal? Cost)> Batch(AppDbContext db, WorkAccessScope scope, Guid batch, Guid location, bool locked, CancellationToken ct)
    {
        var q = locked ? db.ProductStocks.FromSqlInterpolated($"SELECT product_stock.*,xmin FROM public.product_stock WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={batch} AND \"LocationId\"={location} FOR UPDATE")
            : db.ProductStocks.Where(s => s.TenantId == scope.TenantId && s.Id == batch && s.StoreId == location);
        var stock = (await q.ToListAsync(ct)).SingleOrDefault() ?? throw new InventoryPostingNotFoundException();
        var bq = locked ? db.InventoryBalanceProjections.FromSqlInterpolated($"SELECT inventory_balance_projections.*,xmin FROM public.inventory_balance_projections WHERE \"TenantId\"={scope.TenantId} AND \"BatchId\"={batch} FOR UPDATE")
            : db.InventoryBalanceProjections.Where(b => b.TenantId == scope.TenantId && b.BatchId == batch);
        var balance = (await bq.ToListAsync(ct)).SingleOrDefault();
        var sum = await db.InventoryLedgerEntries.Where(e => e.TenantId == scope.TenantId && e.BatchId == batch).SumAsync(e => e.QuantityDelta, ct);
        if (balance == null || balance.ItemId != stock.ProductId || balance.LocationId != location || balance.Quantity != stock.Quantity || balance.ReservedQuantity != 0 || sum != stock.Quantity)
            throw new InventoryPostingConflictException("Transfer source requires a reconciled unreserved batch.");
        var last = await db.InventoryLedgerEntries.SingleOrDefaultAsync(e => e.TenantId == scope.TenantId && e.Id == balance.LastLedgerEntryId && e.BatchId == batch, ct);
        if (last == null || last.QuantityAfter != stock.Quantity) throw new InventoryPostingConflictException("Transfer projection is stale.");
        return (stock, balance, last.UnitCost);
    }
    private static async Task<List<Row>> DispatchRows(AppDbContext db, WorkAccessScope scope, TransferDispatchCommand command, bool locked, CancellationToken ct)
    {
        var rows = new List<Row>();
        foreach (var request in command.Items)
        {
            var (stock, balance, cost) = await Batch(db, scope, request.BatchId, command.FromLocationId, locked, ct);
            if (!StockStatus.IsUsable(stock, DateOnly.FromDateTime(DateTime.UtcNow)) || stock.Quantity < request.Quantity) throw new InventoryPostingConflictException("Insufficient reviewed stock.");
            var itemQuery = locked ? db.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={stock.ProductId} FOR SHARE")
                : db.Items.Where(i => i.TenantId == scope.TenantId && i.Id == stock.ProductId);
            var item = (await itemQuery.ToListAsync(ct)).SingleOrDefault(i => i.IsActive)
                ?? throw new InventoryPostingConflictException("Transfer item is unavailable.");
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 24 || item.Barcodes.FirstOrDefault()?.Length > 128)
                throw new ArgumentException("Invalid item snapshot.");
            var line = new StockTransferItem { TransferId = command.RequestId, ProductStockId = stock.Id, ProductId = stock.ProductId,
                Quantity = request.Quantity, BatchNumber = stock.BatchNumber, ExpiryDate = stock.ExpiryDate };
            rows.Add(new(line, stock, balance, item.Name, item.Unit, item.Barcodes.FirstOrDefault(), cost));
        }
        return rows;
    }
    private static async Task<(BusinessDocument Original, List<Row> Rows)> TerminalRows(AppDbContext db, WorkAccessScope scope, StockTransfer t, string phase, bool locked, CancellationToken ct)
    {
        var original = await db.BusinessDocuments.SingleOrDefaultAsync(d => d.TenantId == scope.TenantId && d.SourceSystem == TransferPostingRules.Source("dispatch") && d.SourceId == t.Id.ToString() && d.Status == "posted", ct)
            ?? throw new InventoryPostingConflictException("Legacy transfers require separate reconciliation, not ERP terminal posting.");
        var items = await db.StockTransferItems.Where(x => x.TransferId == t.Id).OrderBy(x => x.ProductStockId).ToListAsync(ct);
        var lines = await db.DocumentLines.Where(x => x.TenantId == scope.TenantId && x.DocumentId == original.Id).ToListAsync(ct);
        var entries = await db.InventoryLedgerEntries.Where(e => e.TenantId == scope.TenantId && e.DocumentId == original.Id).ToListAsync(ct);
        if (items.Count is < 1 or > 100 || items.Count != lines.Count || items.Count != entries.Count) throw new InventoryPostingConflictException("Incomplete dispatch graph.");
        var rows = new List<Row>();
        foreach (var item in items)
        {
            var line = lines.SingleOrDefault(l => Meta(l, "transferItemId") == item.Id);
            var entry = entries.SingleOrDefault(e => e.DocumentLineId == line?.Id);
            if (line == null || entry == null || entry.QuantityDelta != -item.Quantity || entry.BatchId != item.ProductStockId
                || line.Quantity != item.Quantity || line.ItemId != item.ProductId || line.ExpiryDateSnapshot != item.ExpiryDate || line.BatchNumberSnapshot != item.BatchNumber)
                throw new InventoryPostingConflictException("Dispatch snapshots are inconsistent.");
            ProductStock stock; InventoryBalanceProjection? balance = null;
            if (phase is "cancel" or "receive")
            {
                (stock, balance, _) = await Batch(db, scope, item.ProductStockId, t.FromStoreId, locked, ct);
                if (stock.ProductId != item.ProductId || stock.BatchNumber != item.BatchNumber || stock.ExpiryDate != item.ExpiryDate || stock.ZoneId != entry.ZoneId)
                    throw new InventoryPostingConflictException("Original batch metadata changed; reconcile before returning stock.");
            }
            else stock = new() { Id = item.ProductStockId, TenantId = scope.TenantId, StoreId = t.FromStoreId, ProductId = item.ProductId };
            rows.Add(new(item, stock, balance, line.ItemNameSnapshot, line.Unit, line.BarcodeSnapshot, entry.UnitCost, entry));
        }
        return (original, rows);
    }
    private static Guid Meta(DocumentLine line, string name) { using var json = JsonDocument.Parse(line.Metadata!); return json.RootElement.GetProperty(name).GetGuid(); }
    private static object DispatchIdentity(TransferDispatchCommand c) => new { c.RequestId, c.FromLocationId, c.ToLocationId, c.TransferType, c.Notes,
        items = c.Items.Select(i => new { i.BatchId, quantity = TransferPostingRules.Number(i.Quantity) }).ToArray() };
    private static object[] Snapshots(List<Row> rows, bool current) => rows.Select(r => (object)new { r.Item.ProductStockId, r.Item.ProductId,
        quantity = TransferPostingRules.Number(r.Item.Quantity), available = current ? TransferPostingRules.Number(r.Stock.Quantity) : null,
        r.Name, r.Unit, r.Barcode, r.Item.BatchNumber, r.Item.ExpiryDate, qualityStatus = r.Stock.Status, cost = r.Cost.HasValue ? TransferPostingRules.Number(r.Cost.Value) : null }).ToArray();
    private static string DispatchFingerprint(Guid tenant, TransferDispatchCommand c, List<Row> rows) => ReceiptPostingRules.Hash(new { protocol = "transfer.dispatch.review.v1", tenant, command = DispatchIdentity(c), rows = Snapshots(rows, true) });
    private static string TerminalFingerprint(StockTransfer t, string phase, BusinessDocument d, List<Row> rows) => ReceiptPostingRules.Hash(new { protocol = $"transfer.{phase}.review.v1", t.TenantId, t.Id, t.FromStoreId, t.ToStoreId, t.Status, t.TransferType, t.Notes, dispatchDocumentId = d.Id, d.ContentHash, rows = Snapshots(rows, phase == "cancel") });
    private static TransferReviewedLine[] Review(List<Row> rows, bool current) => rows.Select(r => new TransferReviewedLine(r.Item.ProductStockId, r.Item.ProductId,
        r.Name, r.Unit, r.Item.Quantity, current ? r.Stock.Quantity : null, r.Item.BatchNumber, r.Item.ExpiryDate, r.Cost,
        r.Stock.Status, r.Cost.HasValue ? TransferPostingRules.Number(r.Cost.Value) : null)).ToArray();
}
