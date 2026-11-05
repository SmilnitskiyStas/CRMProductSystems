using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ReceiptCategoryWorkStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access) : IReceiptCategoryWork
{
    private async Task<(AppDbContext Db, WorkAccessScope Scope)> Context(CancellationToken ct)
    {
        var scope = await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        if (!ReceiptPostingRules.CanReceive(scope.Role)) throw new WorkAccessDeniedException();
        return (new AppDbContext(options), scope);
    }
    private static async Task<StockReceipt> Load(AppDbContext db, WorkAccessScope scope, Guid id, bool write, CancellationToken ct)
    {
        await ReceiptPostingStore.Establish(db, scope, ct, requireInventory: false);
        StockReceipt r;
        if (write)
            r = (await db.StockReceipts.FromSqlInterpolated($"SELECT stock_receipts.*, xmin FROM public.stock_receipts WHERE \"TenantId\"={scope.TenantId} AND \"Id\"={id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault()
                ?? throw new InventoryPostingNotFoundException();
        else r = await db.StockReceipts.SingleOrDefaultAsync(x => x.TenantId == scope.TenantId && x.Id == id, ct) ?? throw new InventoryPostingNotFoundException();
        await ReceiptPostingStore.Location(db, scope, r.DestinationStoreId, ct);
        await db.Entry(r).Collection(x => x.Items).Query().Include(x => x.Product).ThenInclude(x => x!.Category).LoadAsync(ct);
        return r;
    }
    private static async Task<ReceiptWorkDto> Describe(AppDbContext db, StockReceipt r, CancellationToken ct)
    {
        var ids = ReceiptCategoryWorkRules.Read(r)?.Groups.Where(x => x.OwnerId != null).Select(x => x.OwnerId!.Value).Distinct().ToArray() ?? [];
        var names = await db.Users.AsNoTracking().Where(x => x.TenantId == r.TenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return ReceiptCategoryWorkRules.Describe(r, names);
    }
    public async Task<ReceiptWorkDto> GetAsync(Guid receiptId, CancellationToken ct)
    {
        var (context, scope) = await Context(ct); await using var db = context;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var result = await Describe(db, await Load(db, scope, receiptId, false, ct), ct);
        await tx.CommitAsync(ct); return result;
    }
    public async Task<ReceiptWorkDto> ChangeAsync(Guid receiptId, string action, string? key, ReceiptCategorySave? save, CancellationToken ct, Guid[]? lineIds = null)
    {
        if (action is not ("start" or "take" or "release" or "reopen" or "save")) throw new ArgumentException("Невідома дія категорії.");
        var (context, scope) = await Context(ct); await using var db = context;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var r = await Load(db, scope, receiptId, true, ct);
        if (!ReceiptCategoryWorkRules.Open(r)) throw new InventoryPostingConflictException("Накладна вже закрита.");
        var state = ReceiptCategoryWorkRules.Read(r);
        ReceiptCategoryGroup? changedGroup = null;
        if (action == "start")
        {
            if (r.Status == "draft" && r.Source == ReceiptSource.AutoReplenishment && r.TakenByUserId is Guid holder && holder != scope.UserId)
                throw new InventoryPostingConflictException("Спочатку власник має звільнити чернетку замовлення.");
            state ??= ReceiptCategoryWorkRules.Create(r);
        }
        else
        {
            if (state == null || !ReceiptCategoryWorkRules.Partition(r, state)) throw new InventoryPostingConflictException("Оновіть розподіл категорій.");
            var index = Array.FindIndex(state.Groups, g => g.Key == key);
            if (index < 0) throw new InventoryPostingNotFoundException();
            var group = state.Groups[index];
            if (action == "take")
            {
                if (group.OwnerId is Guid owner && owner != scope.UserId) throw new InventoryPostingConflictException("Категорію вже взяв інший менеджер.");
                if (lineIds != null)
                {
                    var chosen = lineIds.ToHashSet();
                    if (group.OwnerId != null || chosen.Count == 0 || chosen.Count != lineIds.Length || !chosen.IsSubsetOf(group.LineIds))
                        throw new InventoryPostingConflictException("Оберіть вільні рядки цієї категорії.");
                    if (chosen.Count < group.LineIds.Length)
                    {
                        var remaining = group with { LineIds = group.LineIds.Where(x => !chosen.Contains(x)).ToArray() };
                        state = state with { Groups = [.. state.Groups, remaining] };
                        group = group with { Key = Guid.NewGuid().ToString(), LineIds = group.LineIds.Where(chosen.Contains).ToArray() };
                    }
                }
                group = group with { Revision = group.OwnerId == scope.UserId ? group.Revision : group.Revision + 1, OwnerId = scope.UserId, TakenAt = group.TakenAt ?? DateTime.UtcNow };
            }
            else if (action == "release")
            {
                if (group.OwnerId != null && group.OwnerId != scope.UserId && scope.Role is not ("enterprise_admin" or "network_manager")) throw new WorkAccessDeniedException();
                group = group with { Revision = group.Revision + 1, OwnerId = null, TakenAt = null, CompletedAt = null, CompletedFingerprint = null, LastRequestId = null, LastRequestHash = null };
            }
            else if (action == "reopen")
            {
                if (group.OwnerId != scope.UserId) throw new WorkAccessDeniedException();
                group = group with { Revision = group.Revision + 1, CompletedFingerprint = null, CompletedAt = null, LastRequestId = null, LastRequestHash = null };
            }
            else
            {
                if (group.OwnerId != scope.UserId) throw new InventoryPostingConflictException("Спочатку візьміть цю категорію в роботу.");
                if (save == null || save.RequestId == Guid.Empty || save.Items == null || save.Items.Count != group.LineIds.Length
                    || save.Items.Select(x => x.ItemId).Distinct().Count() != save.Items.Count
                    || !save.Items.Select(x => x.ItemId).ToHashSet().SetEquals(group.LineIds)) throw new ArgumentException("Передайте всі й лише рядки своєї категорії.");
                var requestHash = ReceiptPostingRules.Hash(save with { Items = save.Items.OrderBy(x => x.ItemId).ToList() });
                if (group.LastRequestId == save.RequestId)
                {
                    if (group.LastRequestHash != requestHash) throw new InventoryPostingConflictException("Цей запит вже має інший вміст.");
                    var replay = await Describe(db, r, ct); await tx.CommitAsync(ct); return replay;
                }
                if (group.CompletedFingerprint == ReceiptCategoryWorkRules.Fingerprint(r, group))
                    throw new InventoryPostingConflictException("Спочатку поверніть завершену категорію до перевірки.");
                if (save.ExpectedFingerprint != ReceiptCategoryWorkRules.Fingerprint(r, group)) throw new InventoryPostingConflictException("Рядки категорії змінилися. Оновіть і перевірте їх.");
                var lines = r.Items.ToDictionary(x => x.Id);
                foreach (var edit in save.Items)
                {
                    var line = lines[edit.ItemId];
                    if (edit.QuantityOrdered != null || edit.QuantityReceived is decimal qty && (qty < 0 || qty > 99999999.99m || decimal.Round(qty, 2) != qty)
                        || edit.PricePurchase is decimal price && (price < 0 || price > 9999999999.99m || decimal.Round(price, 2) != price)
                        || edit.BatchNumber?.Length > 100 || edit.DiscrepancyNotes?.Length > 500)
                        throw new ArgumentException("Некоректна кількість, ціна або партія рядка.");
                    if (line.PricePurchase != null && edit.PricePurchase == null)
                        throw new ArgumentException("Відому закупівельну ціну не можна залишити порожньою.");
                    if (save.Complete && (edit.QuantityReceived == null || edit.ExpiryDate == null || line.Product is not { IsActive: true }
                        || line.Product.TenantId != r.TenantId || string.IsNullOrWhiteSpace(line.Product.Name) || string.IsNullOrWhiteSpace(line.Product.Unit)
                        || line.Product.Unit.Length > 24 || line.Product.Barcodes.FirstOrDefault()?.Length > 128))
                        throw new ArgumentException("Перед завершенням перевірте фактичну кількість і термін придатності кожного товару.");
                    if ((edit.QuantityReceived ?? line.QuantityOrdered) * edit.PricePurchase > 9999999999.99m) throw new ArgumentException("Завелика вартість рядка.");
                    line.QuantityReceived = edit.QuantityReceived; line.PricePurchase = edit.PricePurchase; line.ExpiryDate = edit.ExpiryDate;
                    line.BatchNumber = edit.BatchNumber; line.DiscrepancyNotes = edit.DiscrepancyNotes;
                }
                group = group with { Revision = group.Revision + 1 };
                group = group with { CompletedFingerprint = save.Complete ? ReceiptCategoryWorkRules.Fingerprint(r, group) : null,
                    CompletedAt = save.Complete ? DateTime.UtcNow : null, LastRequestId = save.RequestId, LastRequestHash = requestHash };
            }
            state.Groups[index] = group; changedGroup = group;
        }
        ReceiptCategoryWorkRules.Write(r, state);
        db.ActivityLogs.Add(new ActivityLog { TenantId = scope.TenantId, UserId = scope.UserId, Action = "receipt.category." + action,
            EntityType = "stock_receipt", EntityId = r.Id, Meta = JsonSerializer.Serialize(new { key, lineIds, requestId = save?.RequestId, complete = save?.Complete, groups = (changedGroup == null ? state.Groups : new[] { changedGroup }).Select(g => new { g.Key, g.Name, g.LineIds, g.OwnerId, g.Revision, g.CompletedFingerprint }) }) });
        await db.SaveChangesAsync(ct); var result = await Describe(db, r, ct); await tx.CommitAsync(ct); return result;
    }
}
