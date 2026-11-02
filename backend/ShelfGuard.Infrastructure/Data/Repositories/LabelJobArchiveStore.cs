using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LabelJobArchiveStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access, TimeProvider clock) : ILabelJobArchive
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<WorkAccessScope> Scope(AppDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'", ct);
        var s = await ProductionPostingPreviewReader.Establish(db, await access.GetAsync(ct) ?? throw new WorkAccessDeniedException(), ct, TenantRoleCapabilities.LabelsPreview, false, false);
        if (!s.CanStock || !s.Has(TenantRoleCapabilities.LabelsPreview)) throw new WorkAccessDeniedException(); return s;
    }
    internal static bool Explicit(WorkAccessScope s, string key) => s.Permissions.TryGetValue(key, out var v) ? v : s.Capabilities.Contains(key);
    private static async Task Location(AppDbContext db, WorkAccessScope s, Guid location, bool write, CancellationToken ct)
    {
        if (!s.CanLocation(location) || !await db.Locations.AnyAsync(l => l.TenantId == s.TenantId && l.Id == location && (!write || l.IsActive), ct)) throw new WorkAccessDeniedException();
    }
    private static IQueryable<LabelArchivedJob> Visible(AppDbContext db, WorkAccessScope s, Guid location, bool all, int maxJobProtocol)
    {
        var price = s.Permissions.GetValueOrDefault(TenantRoleCapabilities.PricingView, true);
        return db.Set<LabelArchivedJob>().AsNoTracking().Where(j => j.TenantId == s.TenantId && j.LocationId == location && (all || j.CreatedBy == s.UserId) && (!j.HasPrice || price) && (j.JobProtocol == "label-job.v1" || maxJobProtocol == 2));
    }
    private static async Task<LabelJobArchiveView> View(AppDbContext db, LabelArchivedJob j, CancellationToken ct)
    {
        var head = await db.Set<LabelArchivedJobEvent>().AsNoTracking().Where(e => e.TenantId == j.TenantId && e.JobId == j.Id).OrderByDescending(e => e.Seq).FirstOrDefaultAsync(ct);
        if (head?.CheckpointPayload == null) throw Conflict("label_job_incomplete");
        return new(j.Id, j.LocationId, j.CreatedBy, "desktop_client_reported", true, head.Seq, head.Status, head.ReceivedAt, head.CheckpointPayload);
    }
    public async Task<ProductionDesktopPage<LabelJobArchiveView>> ListAsync(Guid locationId, int page, bool allActors, CancellationToken ct, int maxJobProtocol = 1)
    {
        if (page is < 1 or > 100000 || locationId == Guid.Empty || maxJobProtocol is not (1 or 2)) throw new ArgumentException("Invalid archive query.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); var s = await Scope(db, ct); await Location(db, s, locationId, false, ct);
        if (allActors && !Explicit(s, TenantRoleCapabilities.LabelsAudit)) throw new WorkAccessDeniedException();
        var q = Visible(db, s, locationId, allActors, maxJobProtocol); var count = await q.CountAsync(ct); var rows = await q.OrderByDescending(j => j.ReceivedAt).ThenBy(j => j.Id).Skip((page - 1) * 20).Take(20).ToArrayAsync(ct); var result = new List<LabelJobArchiveView>();
        foreach (var row in rows) result.Add(await View(db, row, ct)); return new(result, count, page, 20, (count + 19) / 20);
    }
    public async Task<LabelJobArchiveView> GetAsync(Guid locationId, Guid id, CancellationToken ct, int maxJobProtocol = 1)
    {
        if (maxJobProtocol is not (1 or 2)) throw new ArgumentException("Invalid protocol version.");
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct); var s = await Scope(db, ct); await Location(db, s, locationId, false, ct);
        var j = await Visible(db, s, locationId, Explicit(s, TenantRoleCapabilities.LabelsAudit), maxJobProtocol).SingleOrDefaultAsync(j => j.Id == id, ct) ?? throw new InventoryPostingNotFoundException(); return await View(db, j, ct);
    }
    private static bool LayoutEquals(LabelLayout a, LabelLayout b) => a.Name == b.Name && a.WidthMm == b.WidthMm && a.HeightMm == b.HeightMm && a.Dpi == b.Dpi && a.Elements.SequenceEqual(b.Elements);
    private static async Task Sources(AppDbContext db, WorkAccessScope s, ParsedLabelJob job, CancellationToken ct)
    {
        if (job.Printer is { } binding)
        {
            var id = binding.GetProperty("revisionId").GetGuid(); var p = await db.Set<LabelPrinterRevision>().AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.LocationId == job.LocationId && p.Id == id, ct);
            var data = binding.GetProperty("data").Deserialize<LabelPrinterData>(Json)!;
            if (p == null || p.ProfileKey != binding.GetProperty("profileKey").GetGuid() || p.Version != binding.GetProperty("version").GetInt32() || p.ContentHash != binding.GetProperty("contentHash").GetString() || data != JsonSerializer.Deserialize<LabelPrinterData>(p.Payload, Json)) throw Conflict("label_job_printer_source");
        }
        if (job.TemplateKind == "server")
        {
            var t = await db.Set<LabelTemplateRevision>().AsNoTracking().SingleOrDefaultAsync(t => t.TenantId == s.TenantId && t.Id == job.TemplateId, ct);
            if (t == null || t.TemplateKey != job.TemplateKey || t.Version != job.TemplateVersion || t.ContentHash != job.TemplateHash || !LayoutEquals(job.Layout, LabelTemplateRules.Normalize(JsonSerializer.Deserialize<LabelLayout>(t.Payload, Json)!))) throw Conflict("label_job_template_source");
        }
        if (job.Price is { } price)
        {
            if (!s.Permissions.GetValueOrDefault(TenantRoleCapabilities.PricingView, true)) throw new WorkAccessDeniedException();
            if (!price.TryGetProperty("offer", out var offer) || offer.ValueKind != JsonValueKind.Object || !offer.TryGetProperty("snapshotId", out var raw) || raw.ValueKind != JsonValueKind.String || !Guid.TryParse(raw.GetString(), out var id)) throw new ArgumentException("Missing price archive.");
            var archived = await db.Set<PriceResolutionSnapshot>().AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == s.TenantId && p.Id == id && p.CreatedBy == s.UserId && p.LocationId == job.LocationId, ct);
            if (archived == null || !LabelJobArchiveRules.PriceMatches(price, archived.Payload, archived.ContentHash)) throw Conflict("label_job_price_source");
        }
    }
    public async Task<LabelJobArchiveAck> UploadAsync(LabelJobUpload command, CancellationToken ct)
    {
        var job = LabelJobArchiveRules.Parse(command);
        await using var db = new AppDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(ct); var s = await Scope(db, ct);
        if (job.TenantId != s.TenantId || job.ActorId != s.UserId || !s.Has(TenantRoleCapabilities.LabelsPrint) || job.RequiresBulk && !Explicit(s, TenantRoleCapabilities.LabelsBulk)) throw new WorkAccessDeniedException();
        await Location(db, s, job.LocationId, true, ct); await Sources(db, s, job, ct);
        try
        {
            // Operation and aggregate locks are ordered: same operation cannot race between different jobs.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"labels.jobs.op:" + s.TenantId + ":" + command.OperationId},0))", ct);
            var requestHash = LabelJobArchiveRules.Hash(ProtocolRequest(command, s.UserId));
            var prior = await db.Set<LabelArchivedJobUpload>().AsNoTracking().SingleOrDefaultAsync(u => u.TenantId == s.TenantId && u.Id == command.OperationId, ct);
            if (prior != null)
            {
                if (prior.ReportedBy != s.UserId || prior.RequestHash != requestHash || prior.JobId != job.Id) throw Conflict("label_job_operation_conflict");
                return JsonSerializer.Deserialize<LabelJobArchiveAck>(prior.Result, Json)! with { Replayed = true };
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"labels.jobs.job:" + s.TenantId + ":" + job.Id},0))", ct);
            var at = clock.GetUtcNow().UtcDateTime;
            var header = await db.Set<LabelArchivedJob>().SingleOrDefaultAsync(j => j.TenantId == s.TenantId && j.Id == job.Id, ct);
            if (header == null)
            {
                header = new() { TenantId = s.TenantId, Id = job.Id, LocationId = job.LocationId, CreatedBy = s.UserId, ClientCreatedAt = job.CreatedAt, ReceivedAt = at, CommandHash = job.CommandHash, IntentHash = job.IntentHash, ArtifactHash = job.ArtifactHash, Copies = job.Copies, RequiresBulk = job.RequiresBulk, HasPrice = job.Price != null, JobProtocol = job.JobProtocol, PrinterRevisionId = job.Printer?.GetProperty("revisionId").GetGuid(), InitialPayload = job.Payload };
                db.Add(header); await db.SaveChangesAsync(ct);
            }
            else if (header.LocationId != job.LocationId || header.CreatedBy != s.UserId || header.CommandHash != job.CommandHash || header.IntentHash != job.IntentHash) throw Conflict("label_job_intent_conflict");
            var existing = await db.Set<LabelArchivedJobEvent>().AsNoTracking().Where(e => e.TenantId == s.TenantId && e.JobId == job.Id).OrderBy(e => e.Seq).ToArrayAsync(ct);
            foreach (var e in job.Events.Take(existing.Length)) if (existing[e.Seq - 1].ContentHash != e.ContentHash) throw Conflict("label_job_history_conflict");
            foreach (var e in job.Events.Skip(existing.Length)) db.Add(new LabelArchivedJobEvent { TenantId = s.TenantId, JobId = job.Id, Seq = e.Seq, Status = e.Status, Attempt = e.Attempt, OccurredAt = e.OccurredAt, ReceivedAt = at, ReportedBy = s.UserId, ContentHash = e.ContentHash, Payload = e.Payload, CheckpointPayload = e.Seq == job.Events.Count ? job.Payload : null });
            await db.SaveChangesAsync(ct);
            if (job.Receipt is { } receipt)
            {
                var saved = await db.Set<LabelArchivedJobReceipt>().AsNoTracking().SingleOrDefaultAsync(r => r.TenantId == s.TenantId && r.JobId == job.Id, ct);
                if (saved != null) { if (saved.Payload != receipt.GetRawText()) throw Conflict("label_job_receipt_conflict"); }
                else db.Add(new LabelArchivedJobReceipt { TenantId = s.TenantId, JobId = job.Id, EventSeq = job.Events.Count, ClientAcceptedAt = receipt.GetProperty("acceptedAt").GetDateTimeOffset().UtcDateTime, ReceivedAt = at, CommandHash = job.CommandHash, ArtifactHash = job.ArtifactHash, Copies = job.Copies, Payload = receipt.GetRawText() });
            }
            var head = job.Events.Count >= existing.Length ? job.Events[^1] : new LabelJobArchiveEvent(existing[^1].Seq, existing[^1].Status, existing[^1].Attempt, "", existing[^1].OccurredAt, existing[^1].ContentHash, existing[^1].Payload);
            var ack = new LabelJobArchiveAck(command.OperationId, job.Id, job.CommandHash, job.CheckpointHash, job.Events.Count, head.Seq, head.Status, at, false);
            db.Add(new LabelArchivedJobUpload { Id = command.OperationId, TenantId = s.TenantId, JobId = job.Id, ReportedBy = s.UserId, ReceivedAt = at, RequestHash = requestHash, Result = JsonSerializer.Serialize(ack, Json) });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return ack;
        }
        catch (Exception e) when (e is PostgresException || e.InnerException is PostgresException)
        {
            var p = e as PostgresException ?? (PostgresException)e.InnerException!; if (p.SqlState is "23505" or "55P03" or "40001" or "40P01") throw Conflict("label_job_concurrent"); throw;
        }
    }
    private static string ProtocolRequest(LabelJobUpload c, Guid actor) => JsonSerializer.Serialize(new { protocol = LabelJobArchiveRules.Protocol, c.OperationId, actor, c.Payload }, Json);
    private static PricingConflict Conflict(string code) => new(code, "Архів завдання або джерело не відповідає збереженій історії. Оновіть перегляд; невідомий результат повторюйте тим самим запитом.");
}
