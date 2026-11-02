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

public sealed class WriteOffPostingStore(DbContextOptions<AppDbContext> options, IInventoryPostingGate gate,
    TimeProvider clock, WriteOffPostingOptions writer) : IWriteOffPostingStore
{
    private sealed record Row(ProductStock Stock, InventoryBalanceProjection Balance, WriteOffReviewedBatch Review, InventoryLedgerEntry? Original = null);
    public async Task<WriteOffDraftResult> CreateDraftAsync(WorkAccessScope scope, WriteOffDraftCommand command, CancellationToken ct)
    {
        command = WriteOffPostingRules.NormalizeDraft(command);
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Establish(db, scope, ct); await Location(db, scope, command.LocationId, ct);
        if (!Configured(scope.TenantId, command.LocationId)) throw new InventoryPostingDisabledException();
        if (!await Ready(db, scope.TenantId, command.LocationId, ct)) throw new InventoryPostingConflictException("Write-off fence upgrade required.");
        var key = $"write_off.draft.v1:{scope.TenantId:N}:{command.RequestId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", ct);
        var prior = await db.WriteOffs.Include(w => w.Items).SingleOrDefaultAsync(w => w.TenantId == scope.TenantId && w.Id == command.RequestId, ct);
        if (prior != null)
        {
            if (prior.StoreId != command.LocationId || prior.CreatedBy != scope.UserId || prior.Reason != command.Reason || prior.Notes != command.Notes
                || prior.Items.Count != command.Items.Count || prior.Items.Select(i => i.ProductId).Distinct().Count() != command.Items.Count
                || prior.Items.Any(i => i.ProductStockId.HasValue || i.IsReturnedToSupplier
                    || i.UnitPrice.HasValue || i.LossAmount.HasValue || i.UnitPricePurchase.HasValue || i.LossAmountPurchase.HasValue
                    || i.ReimbursementType != null || i.ReimbursementValue.HasValue || i.ReimbursementAmount.HasValue
                    || !command.Items.Any(x => x.ItemId == i.ProductId && x.Quantity == i.Quantity))
                || prior.TotalLossAmount.HasValue || prior.TotalLossAmountPurchase.HasValue || prior.TotalReimbursementAmount.HasValue)
                throw new InventoryPostingConflictException("Different draft intent or draft source was edited.");
            await tx.CommitAsync(ct); return new(prior.Id, prior.StoreId, true);
        }
        var ids = command.Items.Select(x => x.ItemId).ToArray();
        if (await db.Items.CountAsync(i => i.TenantId == scope.TenantId && ids.Contains(i.Id) && i.IsActive && i.Name != "" && i.Unit != null && i.Unit != "" && i.Unit.Length <= 24, ct) != ids.Length)
            throw new InventoryPostingNotFoundException();
        var draft = new WriteOff { Id = command.RequestId, TenantId = scope.TenantId, StoreId = command.LocationId,
            Status = "pending_approval", Reason = command.Reason, Notes = command.Notes, CreatedBy = scope.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime };
        foreach (var item in command.Items) draft.Items.Add(new WriteOffItem { WriteOffId = draft.Id, ProductId = item.ItemId, Quantity = item.Quantity });
        db.Add(draft);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new InventoryPostingConflictException("Draft request identity already exists."); }
        return new(draft.Id, draft.StoreId, false);
    }
    public async Task<WriteOffPostingPreview> PreviewAsync(WorkAccessScope scope, Guid id, string phase, CancellationToken ct)
    {
        WriteOffPostingRules.Phase(phase);
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct); await Establish(db, scope, ct);
        var w = await Header(db, scope, id, false, ct); await Location(db, scope, w.StoreId, ct);
        var (original, rows) = await Plan(db, scope, w, phase, false, ct);
        var ready = await Ready(db, scope.TenantId, w.StoreId, ct); var configured = Configured(scope.TenantId, w.StoreId);
        var fingerprint = Fingerprint(w, phase, original, rows);
        await tx.CommitAsync(ct); return new(w.Id, w.StoreId, phase, fingerprint, configured, ready, configured && ready, rows.Select(Reviewed).ToArray(), Source(w));
    }
    public async Task<WriteOffPostingResult> PostAsync(WorkAccessScope scope, Guid id, string phase, WriteOffPostingCommand command, CancellationToken ct)
    {
        WriteOffPostingRules.Phase(phase); TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
        command = command with { Reason = WriteOffPostingRules.Reason(phase, command.Reason) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(1)); ct = deadline.Token;
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await Establish(db, scope, ct);
            // Authenticate the actual location before taking a database-wide FEFO lock; do not track this preliminary read.
            var target = await db.WriteOffs.AsNoTracking().Where(w => w.TenantId == scope.TenantId && w.Id == id).Select(w => (Guid?)w.StoreId).SingleOrDefaultAsync(ct)
                ?? throw new InventoryPostingNotFoundException();
            await Location(db, scope, target, ct);
            if (!Configured(scope.TenantId, target)) throw new InventoryPostingDisabledException();
            if (!await Ready(db, scope.TenantId, target, ct)) throw new InventoryPostingConflictException("Write-off fence upgrade required.");
            // Bounded first cut: also excludes phantom earlier-expiry batches and metadata changes during FEFO selection.
            // EXCLUSIVE precedes header/row locks, and conflicts with other stock writers' FOR UPDATE table locks.
            if (phase != "reject") await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.product_stock IN EXCLUSIVE MODE", ct);
            var w = await Header(db, scope, id, true, ct); await Location(db, scope, w.StoreId, ct);
            if (!Configured(scope.TenantId, w.StoreId)) throw new InventoryPostingDisabledException();
            if (!await Ready(db, scope.TenantId, w.StoreId, ct)) throw new InventoryPostingConflictException("Write-off fence upgrade required.");
            var hash = ReceiptPostingRules.Hash(new { protocol = WriteOffPostingRules.Source(phase), command.ExpectedFingerprint, command.Reason });
            var prior = await db.BusinessDocuments.SingleOrDefaultAsync(d => d.TenantId == scope.TenantId && d.SourceId == id.ToString() && d.SourceSystem == WriteOffPostingRules.Source(phase), ct);
            if (prior != null)
            {
                if (phase == "reject")
                {
                    if (prior.ContentHash != hash || prior.Status != "cancelled" || w.Status != "rejected") throw new InventoryPostingConflictException("Different rejection confirmation.");
                    await tx.CommitAsync(ct); return new(id, phase, prior.Id, null, null, true);
                }
                var p = await db.DocumentPostings.SingleAsync(p => p.TenantId == scope.TenantId && p.DocumentId == prior.Id, ct);
                var o = await db.OutboxEvents.SingleAsync(o => o.TenantId == scope.TenantId && o.PostingId == p.Id, ct);
                if (p.RequestHash != hash || !(prior.Status == "posted" || phase == "approve" && prior.Status == "reversed")
                    || phase == "reverse" && w.Status != "reversed" || phase == "approve" && w.Status is not ("approved" or "reversed") || o.DeliveryScope != "live")
                    throw new InventoryPostingConflictException("Different posting confirmation or inconsistent history.");
                ErpEventContracts.FromOutbox(o); await tx.CommitAsync(ct); return new(id, phase, prior.Id, p.Id, o.Id, true);
            }
            var (original, rows) = await Plan(db, scope, w, phase, true, ct);
            if (Fingerprint(w, phase, original, rows) != command.ExpectedFingerprint) throw new InventoryPostingConflictException("Source or FEFO plan changed since review.");
            var time = clock.GetUtcNow().UtcDateTime; var at = new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Utc);
            var doc = new BusinessDocument { TenantId = scope.TenantId, DocumentType = "stock_write_off", SourceSystem = WriteOffPostingRules.Source(phase), SourceId = id.ToString(),
                CreatedBy = scope.UserId, CreatedAt = at, UpdatedAt = at, OccurredAt = at, ReversesDocumentId = phase == "reverse" ? original!.Id : null,
                ContentHash = phase == "reject" ? hash : ReceiptPostingRules.Hash(new { hash, actor = scope.UserId, at }) };
            db.Add(doc); await db.SaveChangesAsync(ct);
            if (phase == "reject")
            {
                doc.Status = "submitted"; await db.SaveChangesAsync(ct);
                db.Add(new DocumentApproval { TenantId = scope.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = scope.UserId, Decision = "rejected", ContentHash = doc.ContentHash!, Reason = command.Reason, CreatedAt = at });
                await db.SaveChangesAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_review_document_id',{doc.Id.ToString()},true)", ct);
                w.Status = "rejected"; await db.SaveChangesAsync(ct); doc.Status = "cancelled"; await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct); return new(id, phase, doc.Id, null, null, false);
            }
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.inventory_reservations IN SHARE MODE", ct);
            var itemIds = rows.Select(x => x.Stock.ProductId).Distinct().ToArray();
            if (await db.InventoryReservations.AnyAsync(r => r.TenantId == scope.TenantId && r.LocationId == w.StoreId && itemIds.Contains(r.ItemId) && r.Status == "active", ct))
                throw new InventoryPostingConflictException("Reserved write-off items require separate handling.");
            var posting = new DocumentPosting { TenantId = scope.TenantId, DocumentId = doc.Id, PerformedBy = scope.UserId, PostedAt = at,
                IdempotencyKey = WriteOffPostingRules.Key(id, phase), RequestHash = hash, ReversesPostingId = phase == "reverse" ? rows[0].Original!.PostingId : null };
            var entries = new List<InventoryLedgerEntry>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; var review = row.Review; var delta = phase == "approve" ? -review.Quantity : review.Quantity;
                var after = row.Stock.Quantity + delta;
                if (!TransferPostingRules.Exact(after)) throw new InventoryPostingConflictException("Stock would underflow or overflow.");
                var value = review.UnitCost.HasValue ? decimal.Round(delta * review.UnitCost.Value, 4, MidpointRounding.AwayFromZero) : (decimal?)null;
                var retail = review.Allocations.All(a => a.RetailLoss.HasValue) ? review.Allocations.Sum(a => a.RetailLoss!.Value) : (decimal?)null;
                if (retail.HasValue && (retail < 0 || retail > 9_999_999_999.99m)) throw new ArgumentException("Compatibility amount overflow.");
                var line = new DocumentLine { TenantId = scope.TenantId, DocumentId = doc.Id, LineNumber = i + 1, ItemId = review.ItemId,
                    FromLocationId = phase == "approve" ? w.StoreId : null, ToLocationId = phase == "reverse" ? w.StoreId : null,
                    Quantity = review.Quantity, Unit = review.Unit, UnitCost = review.UnitCost, ItemNameSnapshot = review.Name, BarcodeSnapshot = review.Barcode,
                    BatchNumberSnapshot = review.BatchNumber, ExpiryDateSnapshot = review.ExpiryDate,
                    Metadata = JsonSerializer.Serialize(new { writeOffId = id, batchId = review.BatchId, allocations = review.Allocations,
                        retailLoss = retail, originalEntryId = row.Original?.Id }) };
                db.Add(line);
                entries.Add(new() { TenantId = scope.TenantId, DocumentId = doc.Id, DocumentLineId = line.Id, PostingId = posting.Id, EntryNumber = i + 1,
                    ItemId = review.ItemId, LocationId = w.StoreId, BatchId = review.BatchId, ZoneId = row.Stock.ZoneId, QuantityBefore = row.Stock.Quantity,
                    QuantityDelta = delta, QuantityAfter = after, UnitCost = review.UnitCost, ValueDelta = value, BatchNumberSnapshot = review.BatchNumber,
                    ExpiryDateSnapshot = review.ExpiryDate, OccurredAt = at, CreatedAt = at, ReversesEntryId = phase == "reverse" ? row.Original!.Id : null });
            }
            await db.SaveChangesAsync(ct); doc.Status = "submitted"; await db.SaveChangesAsync(ct);
            db.Add(new DocumentApproval { TenantId = scope.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = scope.UserId, Decision = "approved", ContentHash = doc.ContentHash!,
                Reason = command.Reason ?? w.Notes ?? "Manager confirmed reviewed expired-stock write-off", CreatedAt = at });
            doc.Status = "approved"; await db.SaveChangesAsync(ct); db.Add(posting); await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{posting.Id.ToString()},true)", ct);
            db.AddRange(entries); await db.SaveChangesAsync(ct);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; var entry = entries[i]; row.Stock.Quantity = entry.QuantityAfter!.Value;
                row.Stock.Status = StockStatus.Recalculate(row.Stock);
                row.Balance.Quantity = row.Stock.Quantity; row.Balance.ReservedQuantity = 0; row.Balance.LastLedgerEntryId = entry.Id; row.Balance.UpdatedAt = at;
                var retail = row.Review.Allocations.All(a => a.RetailLoss.HasValue) ? row.Review.Allocations.Sum(a => a.RetailLoss!.Value) : (decimal?)null;
                db.Add(new StockMovement { TenantId = scope.TenantId, ProductStockId = row.Stock.Id, ProductId = row.Stock.ProductId,
                    MovementType = phase == "approve" ? "write_off" : "adjustment", FromStoreId = phase == "approve" ? w.StoreId : null,
                    FromZoneId = phase == "approve" ? row.Stock.ZoneId : null, ToStoreId = phase == "reverse" ? w.StoreId : null,
                    ToZoneId = phase == "reverse" ? row.Stock.ZoneId : null, Quantity = row.Review.Quantity, QuantityBefore = entry.QuantityBefore,
                    QuantityAfter = entry.QuantityAfter, ReferenceId = id, ReferenceType = phase == "approve" ? "write_off" : "write_off_reversal",
                    TotalAmount = retail, Notes = command.Reason ?? w.Notes, PerformedBy = scope.UserId });
            }
            if (original != null) db.Add(new DocumentLink { TenantId = scope.TenantId, FromDocumentId = original.Id, ToDocumentId = doc.Id, RelationType = "reverses", CreatedAt = at });
            var evt = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(scope.TenantId, Guid.NewGuid(), new(at),
                new(doc.Id, doc.DocumentKey, doc.Version, posting.Id, entries.Select(e => new InventoryLedgerDeltaV1(e.Id, e.EntryNumber, e.ItemId, e.LocationId,
                    e.BatchId, e.QuantityDelta, e.UnitCost, e.ValueDelta, e.Currency, e.ZoneId, e.ReversesEntryId)).ToArray(), posting.ReversesPostingId), correlationId: id));
            db.Add(evt); w.Status = phase == "approve" ? "approved" : "reversed";
            if (phase == "approve") { w.ApprovedBy = scope.UserId; w.ApprovedAt = at; }
            await db.SaveChangesAsync(ct); doc.Status = "posted"; doc.PostedAt = at;
            if (original != null) { original.Status = "reversed"; original.UpdatedAt = at; }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(id, phase, doc.Id, posting.Id, evt.Id, false);
        }
        catch (DbUpdateConcurrencyException) { throw new InventoryPostingConflictException("Concurrent write-off change."); }
        catch (Exception e) when (e is PostgresException || e.InnerException is PostgresException)
        {
            var pg = e as PostgresException ?? (PostgresException)e.InnerException!;
            if (pg.SqlState is "55P03" or "40001" or "40P01" or "23505" || pg.SqlState == "57014" && !ct.IsCancellationRequested)
                throw new InventoryPostingConflictException("Concurrent stock activity; retry the original write-off confirmation.");
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }
    private bool Configured(Guid tenant, Guid location) => writer.Enabled && gate.Allows(tenant, location);
    private static async Task Establish(AppDbContext db, WorkAccessScope scope, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true),set_config('app.erp_posting_id','',true),set_config('app.erp_review_document_id','',true)", ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == scope.UserId && u.TenantId == scope.TenantId && u.IsActive, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == scope.TenantId && t.IsActive, ct);
        if (user == null || user.Role != scope.Role || !InventoryAdjustmentPlanner.CanAdjust(user.Role) || tenant == null || !tenant.HasModule("inventory")) throw new WorkAccessDeniedException();
        if (user.Permissions?.GetValueOrDefault("write-offs", true) == false && !await db.UserPermissionGrants.AnyAsync(g => g.TenantId == scope.TenantId
            && g.UserId == scope.UserId && g.PermissionKey == "write-offs" && g.RevokedAt == null && g.ExpiresAt > DateTime.UtcNow, ct)) throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{user.Role},true)", ct);
    }
    private static async Task Location(AppDbContext db, WorkAccessScope s, Guid location, CancellationToken ct)
    {
        if (!s.CanLocation(location) || !await db.Locations.AnyAsync(l => l.Id == location && l.TenantId == s.TenantId && l.IsActive, ct)
            || s.Role != "enterprise_admin" && !await db.UserLocations.AnyAsync(l => l.TenantId == s.TenantId && l.UserId == s.UserId && l.LocationId == location, ct)) throw new WorkAccessDeniedException();
    }
    private static async Task<bool> Ready(AppDbContext db, Guid tenant, Guid location, CancellationToken ct) =>
        await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        && await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{location}) AS \"Value\"").SingleAsync(ct);
    private static async Task<WriteOff> Header(AppDbContext db, WorkAccessScope scope, Guid id, bool locked, CancellationToken ct)
    {
        var query = locked ? db.WriteOffs.FromSqlInterpolated($"SELECT * FROM public.write_offs WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={id} FOR UPDATE")
            : db.WriteOffs.Where(w => w.TenantId == scope.TenantId && w.Id == id);
        var w = (await query.ToListAsync(ct)).SingleOrDefault() ?? throw new InventoryPostingNotFoundException();
        var items = locked ? await db.WriteOffItems.FromSqlInterpolated($"SELECT * FROM public.write_off_items WHERE \"WriteOffId\"={id} ORDER BY \"Id\" FOR UPDATE").ToListAsync(ct)
            : await db.WriteOffItems.Where(i => i.WriteOffId == id).OrderBy(i => i.Id).ToListAsync(ct);
        foreach (var item in items) if (!w.Items.Contains(item)) w.Items.Add(item);
        return w;
    }
    private static async Task<InventoryBalanceProjection> Balance(AppDbContext db, ProductStock s, CancellationToken ct)
    {
        var b = await db.InventoryBalanceProjections.SingleOrDefaultAsync(b => b.TenantId == s.TenantId && b.BatchId == s.Id, ct);
        var sum = await db.InventoryLedgerEntries.Where(e => e.TenantId == s.TenantId && e.BatchId == s.Id && e.ItemId == s.ProductId && e.LocationId == s.StoreId).SumAsync(e => e.QuantityDelta, ct);
        if (b == null || b.ItemId != s.ProductId || b.LocationId != s.StoreId || b.Quantity != s.Quantity || b.ReservedQuantity != 0 || sum != s.Quantity
            || !await db.InventoryLedgerEntries.AnyAsync(e => e.TenantId == s.TenantId && e.Id == b.LastLedgerEntryId && e.BatchId == s.Id && e.QuantityAfter == s.Quantity, ct))
            throw new InventoryPostingConflictException("Write-off requires reconciled unreserved batches.");
        return b;
    }
    private static async Task<(BusinessDocument? Original, List<Row> Rows)> Plan(AppDbContext db, WorkAccessScope scope, WriteOff w, string phase, bool locked, CancellationToken ct)
    {
        if (phase == "reject") { if (w.Status != "pending_approval") throw new InventoryPostingConflictException("Only a pending write-off can be rejected."); return (null, []); }
        if (phase == "reverse")
        {
            if (w.Status != "approved") throw new InventoryPostingConflictException("Only a posted ERP write-off can be reversed.");
            var original = await db.BusinessDocuments.SingleOrDefaultAsync(d => d.TenantId == scope.TenantId && d.SourceId == w.Id.ToString() && d.SourceSystem == WriteOffPostingRules.Source("approve") && d.Status == "posted", ct)
                ?? throw new InventoryPostingConflictException("Legacy approvals require separate reconciliation.");
            var lines = await db.DocumentLines.Where(l => l.TenantId == scope.TenantId && l.DocumentId == original.Id).OrderBy(l => l.LineNumber).ToListAsync(ct);
            var entries = await db.InventoryLedgerEntries.Where(e => e.TenantId == scope.TenantId && e.DocumentId == original.Id).ToListAsync(ct);
            if (lines.Count is < 1 or > 100 || lines.Count != entries.Count) throw new InventoryPostingConflictException("Incomplete original write-off graph.");
            var result = new List<Row>();
            foreach (var line in lines)
            {
                var entry = entries.Single(e => e.DocumentLineId == line.Id);
                var stock = await db.ProductStocks.SingleOrDefaultAsync(s => s.TenantId == scope.TenantId && s.Id == entry.BatchId && s.StoreId == w.StoreId, ct)
                    ?? throw new InventoryPostingConflictException("Original batch unavailable.");
                if (stock.ProductId != line.ItemId || stock.ExpiryDate != line.ExpiryDateSnapshot || stock.BatchNumber != line.BatchNumberSnapshot || stock.ZoneId != entry.ZoneId)
                    throw new InventoryPostingConflictException("Original batch metadata changed; reconcile before reversal.");
                using var json = JsonDocument.Parse(line.Metadata!);
                var allocations = json.RootElement.GetProperty("allocations").Deserialize<List<WriteOffAllocatedItem>>()!;
                var balance = await Balance(db, stock, ct);
                result.Add(new(stock, balance, new(stock.Id, stock.ProductId, line.ItemNameSnapshot, line.Unit, line.BarcodeSnapshot, stock.BatchNumber,
                    stock.ExpiryDate, stock.ZoneId, stock.Quantity, line.Quantity, entry.UnitCost, allocations), entry));
            }
            return (original, result);
        }
        WriteOffPostingRules.Validate(w);
        var ids = w.Items.Select(i => i.ProductId).Distinct().ToArray();
        var stocks = await db.ProductStocks.Where(s => s.TenantId == scope.TenantId && s.StoreId == w.StoreId && ids.Contains(s.ProductId) && s.Quantity > 0).OrderBy(s => s.Id).ToListAsync(ct);
        var allocationsByBatch = WriteOffPostingRules.Allocate(w, stocks);
        var productsQuery = locked ? db.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={scope.TenantId} AND \"Id\"=ANY({ids}) ORDER BY \"Id\" FOR SHARE")
            : db.Items.Where(i => i.TenantId == scope.TenantId && ids.Contains(i.Id));
        var products = await productsQuery.ToDictionaryAsync(i => i.Id, ct); var rows = new List<Row>();
        foreach (var pair in allocationsByBatch.OrderBy(p => p.Key))
        {
            var stock = stocks.Single(s => s.Id == pair.Key); var item = products.GetValueOrDefault(stock.ProductId);
            if (item == null || !item.IsActive || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 24 || item.Barcodes.FirstOrDefault()?.Length > 128)
                throw new InventoryPostingConflictException("Item snapshot unavailable.");
            var balance = await Balance(db, stock, ct);
            var cost = await db.InventoryLedgerEntries.Where(e => e.TenantId == scope.TenantId && e.Id == balance.LastLedgerEntryId).Select(e => e.UnitCost).SingleAsync(ct);
            rows.Add(new(stock, balance, new(stock.Id, stock.ProductId, item.Name, item.Unit, item.Barcodes.FirstOrDefault(), stock.BatchNumber,
                stock.ExpiryDate, stock.ZoneId, stock.Quantity, pair.Value.Sum(a => a.Quantity), cost, pair.Value)));
        }
        return (null, rows);
    }
    private static WriteOffReviewSource Source(WriteOff w) => new(w.TenantId,w.Id,w.StoreId,w.Status,w.Reason,w.Notes,w.PdfUrl,w.TotalLossAmount,w.TotalLossAmountPurchase,w.TotalReimbursementAmount,
        w.CreatedBy,w.CreatedAt,w.ApprovedBy,w.ApprovedAt,w.Items.Any(i=>!i.LossAmount.HasValue),w.Items.Any(i=>!i.LossAmountPurchase.HasValue),
        w.Items.OrderBy(i=>i.Id).Select(i=>new WriteOffReviewItem(i.Id,i.ProductId,i.ProductStockId,i.Quantity,i.UnitPrice,i.LossAmount,i.UnitPricePurchase,i.LossAmountPurchase,
            i.IsReturnedToSupplier,i.ReimbursementType,i.ReimbursementValue,i.ReimbursementAmount)).ToArray());
    private static string Fingerprint(WriteOff w, string phase, BusinessDocument? original, List<Row> rows) => ReceiptPostingRules.Hash(new {
        protocol = $"write_off.{phase}.review.v1", source=Source(w),
        originalDocumentId = original?.Id, originalHash = original?.ContentHash, batches = rows.Select(Reviewed).ToArray() });
    private static WriteOffReviewedBatch Reviewed(Row row) => row.Review with { QualityStatus = row.Stock.Status,
        ExactUnitCost = row.Review.UnitCost.HasValue ? TransferPostingRules.Number(row.Review.UnitCost.Value) : null };
}
