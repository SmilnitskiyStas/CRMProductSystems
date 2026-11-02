using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ReceiptPostingStore(DbContextOptions<AppDbContext> options, IInventoryPostingGate gate, TimeProvider clock, ReceiptPostingOptions receiving) : IReceiptPostingStore
{
    public async Task<ProductionDesktopPage<ReceiptDesktopSummary>> ListAsync(WorkAccessScope scope, Guid locationId, int page, string search, string status, CancellationToken ct)
    {
        if (locationId == Guid.Empty || page is < 1 or > 100000 || search == null || search.Length > 200 || status is not ("" or "draft" or "ordered" or "in_transit" or "received" or "cancelled")) throw new ArgumentException("Invalid receipt query.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct); await Establish(db, scope, ct, requireInventory: false); await Location(db, scope, locationId, ct);
        var q = db.StockReceipts.AsNoTracking().Where(r => r.TenantId == scope.TenantId && r.DestinationStoreId == locationId);
        if (status.Length > 0) q = q.Where(r => r.Status == status);
        var term = search.Trim(); if (term.Length > 0) q = q.Where(r => r.Id.ToString().Contains(term) || r.Supplier != null && r.Supplier.TenantId == scope.TenantId && EF.Functions.ILike(r.Supplier.Name, "%" + term + "%"));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((page - 1) * 50).Take(50)
            .Select(r => new ReceiptDesktopSummary(r.Id, r.DestinationStoreId, r.Supplier != null && r.Supplier.TenantId == scope.TenantId ? r.Supplier.Name : null, r.Status, r.ExpectedAt, r.Items.Count)).ToListAsync(ct);
        await tx.CommitAsync(ct); return new(rows, count, page, 50, (count + 49) / 50);
    }

    public async Task<ReceiptReceivingContext> PreviewAsync(WorkAccessScope scope, Guid receiptId, CancellationToken ct)
    {
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var inventoryEnabled = await Establish(db, scope, ct, requireInventory: false);
        var r = await db.StockReceipts.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Product)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.TenantId == scope.TenantId, ct) ?? throw new InventoryPostingNotFoundException();
        await Location(db, scope, r.DestinationStoreId, ct);
        var (fenced, ready) = await Ready(db, scope.TenantId, r.DestinationStoreId, ct);
        var eligible = r.Status is "draft" or "ordered" or "in_transit" || r.Status == "received"
            && await db.DocumentPostings.AnyAsync(p => p.TenantId == scope.TenantId && p.IdempotencyKey == ReceiptPostingRules.Key(r.Id), ct);
        await tx.CommitAsync(ct);
        var configured = receiving.Enabled && gate.Allows(scope.TenantId, r.DestinationStoreId);
        return new(r.Id, r.DestinationStoreId, ReceiptPostingRules.Fingerprint(r), configured, fenced,
            configured && ready && inventoryEnabled && eligible && (r.Status == "received" || ReceiptCategoryWorkRules.Read(r) is not { } work || ReceiptCategoryWorkRules.Ready(r, work)) && r.Items.Count is >= 1 and <= ReceiptPostingRules.MaximumLines);
    }

    public async Task<ReceiptPostingResult> ReceiveAsync(WorkAccessScope scope, Guid receiptId, string fingerprint, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(110)); ct = deadline.Token;
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await Establish(db, scope, ct);
            var r = (await db.StockReceipts.FromSqlInterpolated($"SELECT stock_receipts.*, xmin FROM public.stock_receipts WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={receiptId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault()
                ?? throw new InventoryPostingNotFoundException();
            await Location(db, scope, r.DestinationStoreId, ct);
            if (!receiving.Enabled || !gate.Allows(scope.TenantId, r.DestinationStoreId)) throw new InventoryPostingDisabledException();
            if (!(await Ready(db, scope.TenantId, r.DestinationStoreId, ct)).Ready) throw new InventoryPostingConflictException("Receipt fence upgrade is required.");
            var key = ReceiptPostingRules.Key(r.Id);
            var prior = await db.DocumentPostings.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == scope.TenantId && p.IdempotencyKey == key, ct);
            if (prior != null)
            {
                if (prior.RequestHash != fingerprint || r.Status != "received" || r.ReceivedBy != prior.PerformedBy)
                    throw new InventoryPostingConflictException("Receipt belongs to another receiving confirmation.");
                var document = await db.BusinessDocuments.AsNoTracking().SingleAsync(d => d.Id == prior.DocumentId && d.TenantId == scope.TenantId, ct);
                var evt = await db.OutboxEvents.AsNoTracking().SingleAsync(o => o.PostingId == prior.Id && o.TenantId == scope.TenantId, ct);
                if (document.SourceSystem != ReceiptPostingRules.Source || document.SourceId != r.Id.ToString() || document.Status != "posted" || evt.DeliveryScope != "live")
                    throw new InventoryPostingConflictException("Original receipt graph is inconsistent.");
                ErpEventContracts.FromOutbox(evt);
                await tx.CommitAsync(ct); return new(r.Id, document.Id, prior.Id, evt.Id, true);
            }
            var rows = await db.StockReceiptItems.FromSqlInterpolated($"SELECT * FROM public.stock_receipt_items WHERE \"ReceiptId\"={r.Id} ORDER BY \"Id\" FOR UPDATE").ToListAsync(ct);
            var ids = rows.Select(x => x.ProductId).Distinct().ToArray();
            var products = await db.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={scope.TenantId} AND \"Id\"=ANY({ids}) ORDER BY \"Id\" FOR SHARE").ToListAsync(ct);
            var productMap = products.ToDictionary(p => p.Id); var attached = r.Items.Select(x => x.Id).ToHashSet();
            foreach (var line in rows) { line.Product = productMap.GetValueOrDefault(line.ProductId); if (attached.Add(line.Id)) r.Items.Add(line); }
            ReceiptCategoryWorkRules.EnsureReady(r);
            ReceiptPostingRules.Validate(r, scope.UserId);
            if (ReceiptPostingRules.Fingerprint(r) != fingerprint) throw new InventoryPostingConflictException("Receipt changed since review.");
            var largeReceipt = rows.Count > 100;
            if (largeReceipt)
            {
                // A large graph keeps the same constraints. Flush them explicitly below so
                // COMMIT does not inherit Npgsql's shorter internal-command read timeout.
                db.Database.SetCommandTimeout(90);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout='90s'", ct);
            }
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.inventory_reservations IN SHARE MODE", ct);
            if (await db.InventoryReservations.AsNoTracking().AnyAsync(x => x.TenantId == scope.TenantId && x.LocationId == r.DestinationStoreId && ids.Contains(x.ItemId) && x.Status == "active", ct))
                throw new InventoryPostingConflictException("Active reservations are not supported by receiving yet.");
            var time = clock.GetUtcNow().UtcDateTime; var at = new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Utc);
            var doc = new BusinessDocument { TenantId = scope.TenantId, DocumentType = "stock_receipt", SourceSystem = ReceiptPostingRules.Source,
                SourceId = r.Id.ToString(), CreatedBy = scope.UserId, CreatedAt = at, UpdatedAt = at, OccurredAt = at,
                ContentHash = ReceiptPostingRules.Hash(new { protocol = "receipt.receive.content.v1", fingerprint, actor = scope.UserId, at }) };
            var posting = new DocumentPosting { TenantId = scope.TenantId, DocumentId = doc.Id, PerformedBy = scope.UserId,
                RequestHash = fingerprint, IdempotencyKey = key, PostedAt = at };
            db.Add(doc); await db.SaveChangesAsync(ct);
            var incoming = rows.Where(x => (x.QuantityReceived ?? x.QuantityOrdered) > 0).Select((x, index) =>
            {
                var qty = x.QuantityReceived ?? x.QuantityOrdered;
                var stock = new ProductStock { TenantId = scope.TenantId, ProductId = x.ProductId, StoreId = r.DestinationStoreId,
                    Quantity = qty, QuantityInitial = qty, BatchNumber = x.BatchNumber, ExpiryDate = x.ExpiryDate!.Value,
                    SourceType = "receipt", SourceId = r.Id, AddedBy = scope.UserId, AddedAt = at, LastCheckedAt = at,
                    Status = StockStatus.Compute(qty, x.ExpiryDate.Value, at) };
                var line = new DocumentLine { TenantId = scope.TenantId, DocumentId = doc.Id, LineNumber = index + 1,
                    ItemId = x.ProductId, ToLocationId = r.DestinationStoreId, Quantity = qty, Unit = x.Product!.Unit,
                    UnitCost = x.PricePurchase, ItemNameSnapshot = x.Product.Name, BarcodeSnapshot = x.Product.Barcodes.FirstOrDefault(),
                    BatchNumberSnapshot = x.BatchNumber, ExpiryDateSnapshot = x.ExpiryDate,
                    Metadata = JsonSerializer.Serialize(new { receiptId = r.Id, receiptItemId = x.Id, batchId = stock.Id }) };
                var entry = new InventoryLedgerEntry { TenantId = scope.TenantId, DocumentId = doc.Id, PostingId = posting.Id,
                    DocumentLineId = line.Id, EntryNumber = index + 1, ItemId = x.ProductId, LocationId = r.DestinationStoreId,
                    BatchId = stock.Id, QuantityBefore = 0, QuantityDelta = qty, QuantityAfter = qty, UnitCost = x.PricePurchase,
                    ValueDelta = x.PricePurchase.HasValue ? qty * x.PricePurchase : null, BatchNumberSnapshot = x.BatchNumber,
                    ExpiryDateSnapshot = x.ExpiryDate, OccurredAt = at, CreatedAt = at };
                return (Stock: stock, Line: line, Entry: entry);
            }).ToList();
            db.AddRange(incoming.Select(x => x.Line)); await db.SaveChangesAsync(ct);
            doc.Status = "submitted"; await db.SaveChangesAsync(ct);
            db.Add(new DocumentApproval { TenantId = scope.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = scope.UserId,
                Decision = "approved", ContentHash = doc.ContentHash!, Reason = "Receiver confirmed the reviewed receipt", CreatedAt = at });
            doc.Status = "approved"; await db.SaveChangesAsync(ct);
            db.Add(posting); await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{posting.Id.ToString()},true)", ct);
            db.AddRange(incoming.Select(x => x.Stock)); await db.SaveChangesAsync(ct);
            db.AddRange(incoming.Select(x => x.Entry)); await db.SaveChangesAsync(ct);
            foreach (var x in incoming)
            {
                db.Add(new InventoryBalanceProjection { TenantId = scope.TenantId, ItemId = x.Stock.ProductId, LocationId = x.Stock.StoreId,
                    BatchId = x.Stock.Id, Quantity = x.Stock.Quantity, ReservedQuantity = 0, LastLedgerEntryId = x.Entry.Id, UpdatedAt = at });
                db.Add(new StockMovement { TenantId = scope.TenantId, ProductId = x.Stock.ProductId, ProductStockId = x.Stock.Id,
                    ToStoreId = r.DestinationStoreId, MovementType = "receipt", Quantity = x.Stock.Quantity, QuantityBefore = 0,
                    QuantityAfter = x.Stock.Quantity, UnitPrice = x.Entry.UnitCost,
                    TotalAmount = x.Entry.ValueDelta.HasValue ? decimal.Round(x.Entry.ValueDelta.Value, 2, MidpointRounding.AwayFromZero) : null,
                    ReferenceType = "stock_receipt", ReferenceId = r.Id, PerformedBy = scope.UserId });
            }
            var message = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(scope.TenantId, Guid.NewGuid(), new(at),
                new(doc.Id, doc.DocumentKey, doc.Version, posting.Id, incoming.Select(x => new InventoryLedgerDeltaV1(x.Entry.Id,
                    x.Entry.EntryNumber, x.Entry.ItemId, x.Entry.LocationId, x.Entry.BatchId, x.Entry.QuantityDelta, x.Entry.UnitCost,
                    x.Entry.ValueDelta, x.Entry.Currency)).ToArray()), correlationId: r.Id));
            db.Add(message);
            var supplierName = r.SupplierId is Guid supplier ? await db.Suppliers.Where(x => x.Id == supplier && x.TenantId == scope.TenantId).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
            db.Add(new NotificationQueue { TenantId = scope.TenantId, StoreId = r.DestinationStoreId, Channel = "system", EventType = "receipt.created", Status = "pending",
                Title = $"Надходження товару: {supplierName ?? "Постачальник"}", Payload = JsonSerializer.Serialize(new { receiptId = r.Id, supplierId = r.SupplierId,
                    supplierName = supplierName ?? "Постачальник", destinationStoreId = r.DestinationStoreId, itemCount = rows.Count }) });
            r.Status = "received"; r.ReceivedAt = at; r.ReceivedBy = scope.UserId;
            await db.SaveChangesAsync(ct);
            doc.Status = "posted"; doc.PostedAt = at; await db.SaveChangesAsync(ct);
            if (largeReceipt) await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
            await tx.CommitAsync(ct); return new(r.Id, doc.Id, posting.Id, message.Id, false);
        }
        catch (DbUpdateConcurrencyException) { throw new InventoryPostingConflictException("Concurrent receipt change."); }
        catch (Exception e) when (e is PostgresException || e.InnerException is PostgresException)
        {
            var pg = e as PostgresException ?? (PostgresException)e.InnerException!;
            if (pg.SqlState is "55P03" or "40001" or "40P01" or "23505" || pg.SqlState == "57014" && !ct.IsCancellationRequested)
                throw new InventoryPostingConflictException("Concurrent receipt activity or timeout; retry the same receipt confirmation.");
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    internal static async Task<bool> Establish(AppDbContext db, WorkAccessScope scope, CancellationToken ct, bool requireInventory = true)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL search_path=pg_catalog,public; SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true),set_config('app.erp_posting_id','',true)", ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scope.UserId && x.TenantId == scope.TenantId && x.IsActive, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scope.TenantId && x.IsActive, ct);
        if (user == null || user.Role != scope.Role || !ReceiptPostingRules.CanReceive(user.Role)
            || tenant == null || requireInventory && !tenant.HasModule("inventory") || !tenant.HasModule("procurement")) throw new WorkAccessDeniedException();
        if (user.Permissions?.GetValueOrDefault("receipts", true) == false
            && !await db.UserPermissionGrants.AsNoTracking().AnyAsync(g => g.TenantId == scope.TenantId && g.UserId == scope.UserId
                && g.PermissionKey == "receipts" && g.RevokedAt == null && g.ExpiresAt > DateTime.UtcNow, ct))
            throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{user.Role},true)", ct);
        return tenant.HasModule("inventory");
    }
    internal static async Task Location(AppDbContext db, WorkAccessScope scope, Guid location, CancellationToken ct)
    {
        if (!scope.CanLocation(location) || !await db.Locations.AnyAsync(x => x.Id == location && x.TenantId == scope.TenantId && x.IsActive, ct)
            || scope.Role != "enterprise_admin" && !await db.UserLocations.AnyAsync(x => x.TenantId == scope.TenantId && x.LocationId == location && x.UserId == scope.UserId, ct))
            throw new WorkAccessDeniedException();
    }
    private static async Task<(bool Fenced, bool Ready)> Ready(AppDbContext db, Guid tenant, Guid location, CancellationToken ct)
    {
        var fenced = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE \"TenantId\"={tenant} AND \"LocationId\"={location}) AS \"Value\"").SingleAsync(ct);
        var installed = await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct);
        var ready = installed && await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{location}) AS \"Value\"").SingleAsync(ct);
        return (fenced, ready);
    }
}
