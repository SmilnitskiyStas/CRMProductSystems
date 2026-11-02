using System.Data;
using ShelfGuard.Application.Features.Pricing;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PricingDesktopStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access, TimeProvider clock,
    IPosOnlinePricingGate? online = null, IPosOfflinePricingGate? offline = null,
    IInventoryPostingGate? inventory = null, PosInventoryPostingOptions? posOptions = null) : IPricingDesktop
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<WorkAccessScope> Scope(AppDbContext db, CancellationToken ct)
    {
        var s = await ProductionPostingPreviewReader.Establish(db, await access.GetAsync(ct) ?? throw new WorkAccessDeniedException(), ct, TenantRoleCapabilities.PricingView, false, false);
        if (!s.CanStock || !s.Permissions.GetValueOrDefault(TenantRoleCapabilities.PricingView, true)) throw new WorkAccessDeniedException();
        return s;
    }
    private static bool Manage(WorkAccessScope s) => InventoryAdjustmentPlanner.CanAdjust(s.Role) && s.Has(TenantRoleCapabilities.PricingManage);
    private static async Task Location(AppDbContext db, WorkAccessScope s, Guid? id, CancellationToken ct)
    {
        if (id == null) { if (!s.TenantWide) throw new WorkAccessDeniedException(); return; }
        if (!s.CanLocation(id.Value) || !await db.Locations.AnyAsync(l => l.TenantId == s.TenantId && l.Id == id && l.IsActive, ct)) throw new WorkAccessDeniedException();
    }
    private static IQueryable<PriceList> Latest(AppDbContext db, Guid tenant) => db.Set<PriceList>().Where(p => p.TenantId == tenant && !db.Set<PriceList>().Any(n => n.TenantId == tenant && n.ListKey == p.ListKey && n.Version > p.Version));
    private static IQueryable<PriceList> Visible(IQueryable<PriceList> q, WorkAccessScope s) => s.TenantWide ? q : q.Where(p => p.LocationId == null || s.LocationIds.Contains(p.LocationId.Value));
    private static async Task<PricingListView> View(AppDbContext db, PriceList p, CancellationToken ct)
    {
        var lines = await db.Set<PriceListItem>().AsNoTracking().Where(l => l.TenantId == p.TenantId && l.PriceListId == p.Id).OrderBy(l => l.ItemName).ThenBy(l => l.ItemId).Take(101).ToListAsync(ct);
        if (lines.Count is < 1 or > 100) throw new PricingConflict("pricing_incomplete", "Прайс неповний. Зверніться до адміністратора.");
        var location = p.LocationId == null ? null : await db.Locations.Where(l => l.TenantId == p.TenantId && l.Id == p.LocationId).Select(l => l.Name).SingleOrDefaultAsync(ct);
        return new(p.Id, p.ListKey, p.Version, p.DocumentId, p.Fingerprint, p.Name, p.Currency, p.LocationId, location, p.Priority, p.IsActive, p.ValidFrom, p.ValidUntil, lines.Select(l => new PricingItemView(l.ItemId, l.ItemName, l.Unit, l.Price, l.VatRate, l.ItemFingerprint)).ToArray());
    }
    public async Task<PricingAccess> AccessAsync(CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        var ids = (online?.Locations(s.TenantId) ?? []).Concat(offline?.Locations(s.TenantId) ?? []).Distinct().ToArray();
        var scopes = new List<PricingPosScope>();
        if (ids.Length > 0)
        {
            var locations = await db.Locations.AsNoTracking().Where(l => l.TenantId == s.TenantId && ids.Contains(l.Id)
                && (s.TenantWide || s.LocationIds.Contains(l.Id))).OrderBy(l => l.Name).ToArrayAsync(ct);
            foreach (var location in locations) scopes.Add(new(location.Id, location.Name,
                online?.Allows(s.TenantId, location.Id) == true, offline?.Allows(s.TenantId, location.Id) == true,
                location.IsActive && inventory?.Allows(s.TenantId, location.Id) == true && posOptions?.Enabled == true
                    && await IntegratedScopeReadiness.Ready(db, s.TenantId, location.Id, ct)));
        }
        await tx.CommitAsync(ct); return new(Manage(s), true, scopes.Count == 0 ? "planning_only" : "scoped_pos", scopes);
    }
    public async Task<ProductionDesktopPage<PricingListView>> ListsAsync(int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000) throw new ArgumentException("Invalid page.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'", ct); var s = await Scope(db, ct);
        var q = Visible(Latest(db, s.TenantId), s).AsNoTracking(); var count = await q.CountAsync(ct); var rows = await q.OrderBy(p => p.Name).ThenBy(p => p.ListKey).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct); var result = new List<PricingListView>();
        foreach (var p in rows) result.Add(await View(db, p, ct)); await tx.CommitAsync(ct); return new(result, count, page, 20, (count + 19) / 20);
    }
    public async Task<PricingListView> ListAsync(Guid key, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); var s = await Scope(db, ct); var p = await Visible(Latest(db, s.TenantId), s).SingleOrDefaultAsync(p => p.ListKey == key, ct) ?? throw new InventoryPostingNotFoundException(); var result = await View(db, p, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<PricingResult> SaveAsync(PricingSave command, CancellationToken ct)
    {
        var c = PricingRules.Normalize(command); await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct); if (!Manage(s)) throw new WorkAccessDeniedException();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE public.price_lists,public.price_list_items IN SHARE ROW EXCLUSIVE MODE; LOCK TABLE public.items IN SHARE MODE", ct);
            await Location(db, s, c.Data.LocationId, ct);
            var hash = ReceiptPostingRules.Hash(new { protocol = PricingRules.Protocol, c, actor = s.UserId });
            var prior = await db.BusinessDocuments.SingleOrDefaultAsync(d => d.TenantId == s.TenantId && d.DocumentKey == c.OperationId, ct);
            if (prior != null)
            {
                if (prior.SourceSystem != "pricing.plan.v1" || prior.ContentHash != hash || prior.CreatedBy != s.UserId || prior.Status != "approved") throw Conflict("pricing_command_conflict", "Запит уже використано для іншого рішення.");
                var saved = await db.Set<PriceList>().SingleAsync(p => p.TenantId == s.TenantId && p.DocumentId == prior.Id, ct); await tx.CommitAsync(ct); return new(saved.Id, saved.ListKey, saved.Version, prior.Id, true);
            }
            PriceList? old = null;
            if (c.ListKey is Guid key)
            {
                old = await Latest(db, s.TenantId).SingleOrDefaultAsync(p => p.ListKey == key, ct) ?? throw new InventoryPostingNotFoundException(); await Location(db, s, old.LocationId, ct);
                if (old.Fingerprint != c.ExpectedFingerprint) throw Conflict("pricing_changed", "Прайс уже має нову редакцію. Оновіть перегляд.");
            }
            var ids = c.Data.Lines.Select(l => l.ItemId).ToArray(); var items = await db.Items.Where(i => i.TenantId == s.TenantId && ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            if (items.Count != ids.Length || items.Values.Any(i => !i.IsActive || i.VatRate is < 0 or > 100)) throw Conflict("pricing_items", "Виберіть активні товари з коректною ставкою ПДВ.");
            if (c.Data.Lines.Any(l => CatalogDesktopStore.View(items[l.ItemId]).Fingerprint != l.ItemFingerprint)) throw Conflict("pricing_items_changed", "Картки товарів змінилися після перегляду. Перевірте ціни ще раз.");
            var at = clock.GetUtcNow().UtcDateTime; var doc = new BusinessDocument { TenantId = s.TenantId, DocumentKey = c.OperationId, SourceSystem = "pricing.plan.v1", SourceId = c.OperationId.ToString(), Number = c.OperationId.ToString(), DocumentType = "price_plan", CreatedBy = s.UserId, ContentHash = hash, CreatedAt = at, UpdatedAt = at, OccurredAt = at }; db.Add(doc); await db.SaveChangesAsync(ct);
            var entries = c.Data.Lines.Select(l => new PriceListItem { TenantId = s.TenantId, PriceListId = c.OperationId, ItemId = l.ItemId, Price = l.Price, VatRate = items[l.ItemId].VatRate, Unit = items[l.ItemId].Unit, ItemName = items[l.ItemId].Name, ItemFingerprint = l.ItemFingerprint }).ToArray();
            var version = checked((old?.Version ?? 0) + 1); var listKey = c.ListKey ?? c.OperationId; var fingerprint = ReceiptPostingRules.Hash(new { c.OperationId, listKey, version, c.Data, lines = entries.Select(l => new { l.ItemId, l.Price, l.VatRate, l.Unit, l.ItemName, l.ItemFingerprint }) });
            var list = new PriceList { Id = c.OperationId, TenantId = s.TenantId, ListKey = listKey, Version = version, DocumentId = doc.Id, Name = c.Data.Name, LocationId = c.Data.LocationId, Priority = c.Data.Priority, IsActive = c.Data.IsActive, ValidFrom = c.Data.ValidFrom.UtcDateTime, ValidUntil = c.Data.ValidUntil?.UtcDateTime, Fingerprint = fingerprint, CreatedBy = s.UserId, CreatedAt = at }; db.Add(list); db.AddRange(entries);
            var n = 0; foreach (var l in entries) db.Add(new DocumentLine { TenantId = s.TenantId, DocumentId = doc.Id, LineNumber = ++n, ItemId = l.ItemId, Quantity = 1, Unit = l.Unit, UnitPrice = l.Price, VatRate = l.VatRate, ItemNameSnapshot = l.ItemName, Metadata = JsonSerializer.Serialize(new { mode = "planning_only", listKey, version, c, fingerprint }, Json) });
            await db.SaveChangesAsync(ct); doc.Status = "submitted"; await db.SaveChangesAsync(ct); db.Add(new DocumentApproval { TenantId = s.TenantId, DocumentId = doc.Id, Stage = 1, ApproverId = s.UserId, Decision = "approved", ContentHash = hash, Reason = c.Reason, CreatedAt = at }); doc.Status = "approved"; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(list.Id, list.ListKey, list.Version, doc.Id, false);
        }
        catch (Exception e) when (e is PostgresException || e.InnerException is PostgresException)
        {
            var p = e as PostgresException ?? (PostgresException)e.InnerException!; if (p.SqlState is "23505" or "55P03" or "40001" or "40P01") throw Conflict("pricing_concurrent", "Інший користувач змінює прайс. Перевірте збережену операцію."); throw;
        }
    }
    private static PricingConflict Conflict(string code, string message) => new(code, message);
    private static PricingSnapshotView Snapshot(PriceResolutionSnapshot s, bool replay) => new(JsonSerializer.Deserialize<PricingOffer>(s.Payload, Json) ?? throw new InvalidOperationException("Incomplete snapshot."), s.ContentHash, replay);
    public async Task<PricingSnapshotView> SnapshotAsync(Guid id, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct); var row = await db.Set<PriceResolutionSnapshot>().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.Id == id && p.CreatedBy == s.UserId, ct) ?? throw new InventoryPostingNotFoundException(); await Location(db, s, row.LocationId, ct); await tx.CommitAsync(ct); return Snapshot(row, true);
    }
    public async Task<PricingSnapshotView> PreviewAsync(PricingPreview command, CancellationToken ct)
    {
        var c = PricingRules.Normalize(command); await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct); await Location(db, s, c.LocationId, ct);
        // Freeze price and item sources across all reads; replay remains independent of current source values.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'; LOCK TABLE public.price_lists,public.items IN SHARE MODE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({s.TenantId.ToString()+c.OperationId},0))", ct);
        var hash = ReceiptPostingRules.Hash(new { protocol = PricingRules.Protocol, c, actor = s.UserId }); var prior = await db.Set<PriceResolutionSnapshot>().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.Id == c.OperationId, ct);
        if (prior != null) { if (prior.RequestHash != hash || prior.CreatedBy != s.UserId) throw Conflict("pricing_command_conflict", "Запит перегляду вже використано з іншими параметрами."); await tx.CommitAsync(ct); return Snapshot(prior, true); }
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.TenantId == s.TenantId && i.Id == c.ItemId && i.IsActive, ct) ?? throw new InventoryPostingNotFoundException();
        var instant = c.At.UtcDateTime;
        var heads = Latest(db, s.TenantId).Where(p => p.IsActive && (p.LocationId == null || p.LocationId == c.LocationId));
        var choices = await heads.Join(db.Set<PriceListItem>().Where(l => l.TenantId == s.TenantId && l.ItemId == item.Id), p => p.Id, l => l.PriceListId, (p, l) => new { List = p, Line = l }).Take(1001).ToArrayAsync(ct);
        if (choices.Length > 1000) throw Conflict("pricing_limit", "Забагато прайсів для товару. Зменште кількість активних списків.");
        var selection = PlannedBasePricing.Select(s.TenantId, c.LocationId, item.Id, instant,
            choices.Select(x => new BasePriceCandidate(x.List, x.Line)).ToArray());
        var selected = selection.Selected;
        var warnings = new List<string> { "planning_only", "promotions_loyalty_and_basket_not_evaluated", "not_for_retail_printing", "current_revisions_not_historical_checkout" };
        decimal? price = selected?.Line.Price ?? item.PriceRetail; var vat = selected?.Line.VatRate ?? item.VatRate;
        if (selected != null && selected.Line.Unit != item.Unit) { price = null; warnings.Add("unit_changed_requires_new_revision"); }
        if (item.PriceRetail < 0 || price < 0 || vat is < 0 or > 100) { price = null; warnings.Add("invalid_source_values"); }
        if (price == null) warnings.Add("missing_price"); if (price == 0) warnings.Add("zero_price");
        var offer = new PricingOffer(c.OperationId, s.TenantId, c.LocationId, item.Id, item.Name, item.Unit, "UAH", instant, clock.GetUtcNow().UtcDateTime, price, item.PriceRetail, vat, item.VatRate, selected?.List.Id, selected?.List.ListKey, selected?.List.Version, selected == null ? "legacy_item_base" : "planned_price_list", PricingRules.Policy, selection.NextBoundary, price != item.PriceRetail || vat != item.VatRate, warnings);
        var payload = JsonSerializer.Serialize(offer, Json); var snapshot = new PriceResolutionSnapshot { Id = c.OperationId, TenantId = s.TenantId, CreatedBy = s.UserId, LocationId = c.LocationId, ItemId = item.Id, EvaluatedAt = instant, CreatedAt = offer.CreatedAt, RequestHash = hash, ContentHash = ReceiptPostingRules.Hash(offer), Payload = payload }; db.Add(snapshot); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(offer, snapshot.ContentHash, false);
    }
}
