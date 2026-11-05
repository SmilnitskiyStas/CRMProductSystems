using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LabelTemplateStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access, TimeProvider clock) : ILabelTemplates
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<WorkAccessScope> Scope(AppDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'", ct);
        var s = await ProductionPostingPreviewReader.Establish(db, await access.GetAsync(ct) ?? throw new WorkAccessDeniedException(), ct, TenantRoleCapabilities.LabelsPreview, false, false);
        if (!s.CanStock || !s.Has(TenantRoleCapabilities.LabelsPreview)) throw new WorkAccessDeniedException();
        return s;
    }
    private static bool Manage(WorkAccessScope s) => s.Has(TenantRoleCapabilities.LabelsTemplatesManage);
    private static async Task Location(AppDbContext db, WorkAccessScope s, Guid id, CancellationToken ct)
    {
        if (!s.CanLocation(id) || !await db.Locations.AnyAsync(l => l.TenantId == s.TenantId && l.Id == id && l.IsActive, ct)) throw new WorkAccessDeniedException();
    }
    private static LabelTemplateView View(LabelTemplateRevision r) => new(r.Id, r.TemplateKey, r.Version, r.CreatedBy, r.CreatedAt, r.ContentHash, JsonSerializer.Deserialize<LabelLayout>(r.Payload, Json)!);
    private static LabelAssignmentView View(LabelStoreAssignment a) => new(a.Id, a.LocationId, a.Version, a.RevisionId, a.CreatedBy, a.CreatedAt);
    public async Task<LabelCapabilities> AccessAsync(CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        var print = s.Has(TenantRoleCapabilities.LabelsPrint);
        // Bulk requires an explicit role capability or override; rank defaults are insufficient.
        var bulk = s.Permissions.TryGetValue(TenantRoleCapabilities.LabelsBulk, out var grant) ? grant : s.Capabilities.Contains(TenantRoleCapabilities.LabelsBulk);
        return new(true, s.TenantWide && Manage(s), Manage(s), true, print, print && bulk, LabelJobArchiveStore.Explicit(s, TenantRoleCapabilities.LabelsAudit), true, true, LabelJobArchiveStore.Explicit(s, TenantRoleCapabilities.LabelsPrintersManage), 2);
    }
    public Task<ProductionDesktopPage<LabelTemplateView>> ListAsync(int page, CancellationToken ct) => ReadAsync(null, page, ct);
    public async Task<LabelTemplateView> RevisionAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ArgumentException("Missing revision.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        var row = await db.Set<LabelTemplateRevision>().AsNoTracking().SingleOrDefaultAsync(r => r.TenantId == s.TenantId && r.Id == id, ct) ?? throw new InventoryPostingNotFoundException();
        return View(row);
    }
    public Task<ProductionDesktopPage<LabelTemplateView>> HistoryAsync(Guid key, int page, CancellationToken ct) => key == Guid.Empty ? throw new ArgumentException("Missing key.") : ReadAsync(key, page, ct);
    private async Task<ProductionDesktopPage<LabelTemplateView>> ReadAsync(Guid? key, int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000) throw new ArgumentException("Invalid page.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); var s = await Scope(db, ct);
        var q = db.Set<LabelTemplateRevision>().AsNoTracking().Where(r => r.TenantId == s.TenantId);
        q = key == null ? q.Where(r => !db.Set<LabelTemplateRevision>().Any(n => n.TenantId == r.TenantId && n.TemplateKey == r.TemplateKey && n.Version > r.Version)) : q.Where(r => r.TemplateKey == key);
        var count = await q.CountAsync(ct); var rows = await q.OrderBy(r => r.TemplateKey).ThenByDescending(r => r.Version).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct);
        return new(rows.Select(View).ToArray(), count, page, 20, (count + 19) / 20);
    }
    public async Task<LabelTemplateResult> SaveAsync(LabelTemplateSave command, CancellationToken ct)
    {
        var c = LabelTemplateRules.Normalize(command);
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        if (!s.TenantWide || !Manage(s)) throw new WorkAccessDeniedException();
        // Per-tenant locks serialize version checks and exact replay without locking other tenants.
        await Lock(db, s, ct);
        var hash = ReceiptPostingRules.Hash(new { protocol = LabelTemplateRules.Protocol, kind = "template", c, actor = s.UserId });
        var prior = await db.Set<LabelTemplateRevision>().SingleOrDefaultAsync(r => r.TenantId == s.TenantId && r.Id == c.OperationId, ct);
        if (prior != null)
        {
            if (prior.RequestHash != hash || prior.CreatedBy != s.UserId) throw Conflict("label_operation_conflict");
            return new(View(prior), true);
        }
        LabelTemplateRevision? head = null;
        if (c.TemplateKey is Guid key)
        {
            head = await db.Set<LabelTemplateRevision>().Where(r => r.TenantId == s.TenantId && r.TemplateKey == key).OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct) ?? throw new InventoryPostingNotFoundException();
            if (head.Version != c.ExpectedVersion) throw Conflict("label_version_changed");
        }
        var row = new LabelTemplateRevision { Id = c.OperationId, TenantId = s.TenantId, TemplateKey = c.TemplateKey ?? c.OperationId, Version = (head?.Version ?? 0) + 1, CreatedBy = s.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime, RequestHash = hash, ContentHash = ReceiptPostingRules.Hash(c.Data), Payload = JsonSerializer.Serialize(c.Data, Json) };
        db.Add(row); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(View(row), false);
    }
    public async Task<LabelAssignmentView?> AssignmentAsync(Guid location, CancellationToken ct)
    {
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct); await Location(db, s, location, ct);
        var row = await db.Set<LabelStoreAssignment>().Where(a => a.TenantId == s.TenantId && a.LocationId == location).OrderByDescending(a => a.Version).FirstOrDefaultAsync(ct);
        return row == null ? null : View(row);
    }
    public async Task<LabelAssignmentResult> AssignAsync(LabelAssignmentSave command, CancellationToken ct)
    {
        var c = LabelTemplateRules.Normalize(command);
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        if (!Manage(s)) throw new WorkAccessDeniedException(); await Location(db, s, c.LocationId, ct); await Lock(db, s, ct);
        var hash = ReceiptPostingRules.Hash(new { protocol = LabelTemplateRules.Protocol, kind = "assignment", c, actor = s.UserId });
        var prior = await db.Set<LabelStoreAssignment>().SingleOrDefaultAsync(a => a.TenantId == s.TenantId && a.Id == c.OperationId, ct);
        if (prior != null)
        {
            if (prior.RequestHash != hash || prior.CreatedBy != s.UserId) throw Conflict("label_operation_conflict");
            return new(View(prior), true);
        }
        var head = await db.Set<LabelStoreAssignment>().Where(a => a.TenantId == s.TenantId && a.LocationId == c.LocationId).OrderByDescending(a => a.Version).FirstOrDefaultAsync(ct);
        if (head?.Id != c.ExpectedAssignmentId) throw Conflict("label_assignment_changed");
        if (c.RevisionId is Guid revision && !await db.Set<LabelTemplateRevision>().AnyAsync(r => r.TenantId == s.TenantId && r.Id == revision, ct)) throw new InventoryPostingNotFoundException();
        var row = new LabelStoreAssignment { Id = c.OperationId, TenantId = s.TenantId, LocationId = c.LocationId, Version = checked((head?.Version ?? 0) + 1), RevisionId = c.RevisionId, CreatedBy = s.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime, RequestHash = hash };
        db.Add(row); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(View(row), false);
    }
    private static async Task Lock(AppDbContext db, WorkAccessScope s, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"labels.templates.v1:" + s.TenantId},0))", ct);
    private static PricingConflict Conflict(string code) => new(code, "Шаблон або призначення змінилися. Оновіть перегляд; повторюйте невідомий результат лише зі збереженим ідентифікатором запиту.");
}
