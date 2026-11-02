using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;
public sealed class LabelPrinterProfileStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access, TimeProvider clock) : ILabelPrinterProfiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<WorkAccessScope> Scope(AppDbContext db, Guid location, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'", ct);
        var s = await ProductionPostingPreviewReader.Establish(db, await access.GetAsync(ct) ?? throw new WorkAccessDeniedException(), ct, TenantRoleCapabilities.LabelsPreview, false, false);
        if (!s.CanStock || !s.Has(TenantRoleCapabilities.LabelsPreview) || !s.CanLocation(location) || !await db.Locations.AnyAsync(l => l.TenantId == s.TenantId && l.Id == location && l.IsActive, ct)) throw new WorkAccessDeniedException(); return s;
    }
    private static LabelPrinterView View(LabelPrinterRevision p) => new(p.Id, p.ProfileKey, p.Version, p.LocationId, p.CreatedBy, p.CreatedAt, p.ContentHash, JsonSerializer.Deserialize<LabelPrinterData>(p.Payload, Json)!);
    public Task<ProductionDesktopPage<LabelPrinterView>> ListAsync(Guid locationId, int page, CancellationToken ct) => Read(locationId, null, page, ct);
    public Task<ProductionDesktopPage<LabelPrinterView>> HistoryAsync(Guid locationId, Guid key, int page, CancellationToken ct) => Read(locationId, key == Guid.Empty ? throw new ArgumentException("Missing profile.") : key, page, ct);
    private async Task<ProductionDesktopPage<LabelPrinterView>> Read(Guid location, Guid? key, int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000) throw new ArgumentException("Invalid page.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); var s = await Scope(db, location, ct);
        var q = db.Set<LabelPrinterRevision>().AsNoTracking().Where(p => p.TenantId == s.TenantId && p.LocationId == location);
        q = key == null ? q.Where(p => !db.Set<LabelPrinterRevision>().Any(n => n.TenantId == p.TenantId && n.ProfileKey == p.ProfileKey && n.Version > p.Version)) : q.Where(p => p.ProfileKey == key);
        var count = await q.CountAsync(ct); var rows = await q.OrderBy(p => p.ProfileKey).ThenByDescending(p => p.Version).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct); return new(rows.Select(View).ToArray(), count, page, 20, (count + 19) / 20);
    }
    public async Task<LabelPrinterView> RevisionAsync(Guid locationId, Guid id, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, locationId, ct);
        return View(await db.Set<LabelPrinterRevision>().AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.LocationId == locationId && p.Id == id, ct) ?? throw new InventoryPostingNotFoundException());
    }
    public async Task<LabelPrinterResult> SaveAsync(LabelPrinterSave command, CancellationToken ct)
    {
        var c = LabelPrinterRules.Normalize(command); await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, c.LocationId, ct);
        if (!LabelJobArchiveStore.Explicit(s, TenantRoleCapabilities.LabelsPrintersManage)) throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"labels.printers.v1:" + s.TenantId},0))", ct);
        var hash = ReceiptPostingRules.Hash(new { protocol = LabelPrinterRules.Protocol, c, actor = s.UserId });
        var prior = await db.Set<LabelPrinterRevision>().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.Id == c.OperationId, ct);
        if (prior != null) { if (prior.CreatedBy != s.UserId || prior.RequestHash != hash) throw new PricingConflict("printer_operation_conflict", "Запит профілю вже використано з іншими даними."); return new(View(prior), true); }
        LabelPrinterRevision? head = null;
        if (c.ProfileKey is Guid key)
        {
            head = await db.Set<LabelPrinterRevision>().Where(p => p.TenantId == s.TenantId && p.ProfileKey == key).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct) ?? throw new InventoryPostingNotFoundException();
            if (head.LocationId != c.LocationId) throw new WorkAccessDeniedException();
            if (head.Version != c.ExpectedVersion) throw new PricingConflict("printer_version_changed", "Профіль уже має нову редакцію. Оновіть перегляд.");
        }
        var row = new LabelPrinterRevision { Id = c.OperationId, TenantId = s.TenantId, ProfileKey = c.ProfileKey ?? c.OperationId, LocationId = c.LocationId, Version = (head?.Version ?? 0) + 1, CreatedBy = s.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime, ContentHash = ReceiptPostingRules.Hash(c.Data), RequestHash = hash, Payload = JsonSerializer.Serialize(c.Data, Json) };
        db.Add(row); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(View(row), false);
    }
}
