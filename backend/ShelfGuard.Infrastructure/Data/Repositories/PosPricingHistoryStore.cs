using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PosPricingHistoryStore(DbContextOptions<AppDbContext> options, IWorkAccessContext access) : IPosPricingHistory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<WorkAccessScope> Scope(AppDbContext db, CancellationToken ct)
    {
        var s = await ProductionPostingPreviewReader.Establish(db, await access.GetAsync(ct)
            ?? throw new WorkAccessDeniedException(), ct, TenantRoleCapabilities.PricingView, false, false);
        if (!s.CanStock || !s.Permissions.GetValueOrDefault(TenantRoleCapabilities.PricingView, true)) throw new WorkAccessDeniedException();
        return s;
    }
    public async Task<PosPricingHistoryView> SnapshotAsync(Guid transactionId, CancellationToken ct)
    {
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'", ct);
        var s = await Scope(db, ct);
        var row = await db.Set<PosPricingSnapshot>().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == s.TenantId
            && a.TransactionId == transactionId && (s.TenantWide || s.LocationIds.Contains(a.StoreId)), ct)
            ?? throw new InventoryPostingNotFoundException();
        if (PosPricingCapture.Hash(row.Payload) != row.ContentHash) throw new PricingConflict("archive_corrupt", "Контрольна сума архіву не збігається.");
        var comparison = await CompareAsync(db, row, ct);
        var view = new PosPricingHistoryView(row.TransactionId, row.Protocol, row.Channel, row.EvaluatedAt,
            row.RecordedAt, row.ContentHash, row.Payload, comparison);
        await tx.CommitAsync(ct); return view;
    }
    private static PricingShadowReport Compare(PosPricingSnapshot row)
    {
        if (PosPricingCapture.Hash(row.Payload) != row.ContentHash) return new("error", ["archive_hash"], null, null);
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(row.Payload, Json)!;
        if (d.Lines.Any(l => l.BaseResolution is { } basis && !PlannedBasePricing.Matches(basis, l.Input, d.EvaluatedAt, d.FrozenBase)))
            return new("mismatch", ["base_resolution"], null, null);
        var input = new BasketPricingInput(row.TenantId, row.StoreId, new DateTimeOffset(d.EvaluatedAt, TimeSpan.Zero),
            d.TimeZone, d.FrozenBase, d.Lines.Select(l => l.Input).ToArray(),
            d.Rules.Where(r => r.Valid).Select(r => new PromotionRuleInput(r.RuleId, r.VersionId, r.Name, r.CreatedAt, r.Definition!)).ToArray(),
            d.Loyalty, d.Rules.Count(r => !r.Valid));
        return PricingShadow.Compare(input, d.Result);
    }
    private static async Task<PricingShadowReport> CompareAsync(AppDbContext db, PosPricingSnapshot row, CancellationToken ct)
    {
        var result = Compare(row);
        if (result.Outcome != "matched") return result;
        var decision = JsonSerializer.Deserialize<PricingArchivedDecision>(row.Payload, Json)!;
        var planned = decision.Lines.Where(l => l.BaseResolution?.Source == "planned_price_list").ToArray();
        if (planned.Length == 0) return result;
        if (planned.Length > 100) return new("limit", ["base_resolution_limit"], null, null);
        var lists = planned.Select(l => l.BaseResolution!.PriceListId!.Value).Distinct().ToArray();
        var items = planned.Select(l => l.Input.ItemId).Distinct().ToArray();
        // These are append-only source revisions, never the current catalog or latest price list.
        var sources = await db.Set<PriceList>().AsNoTracking().Where(p => p.TenantId == row.TenantId && lists.Contains(p.Id))
            .Join(db.Set<PriceListItem>().AsNoTracking().Where(l => l.TenantId == row.TenantId && items.Contains(l.ItemId)),
                p => p.Id, l => l.PriceListId, (p, l) => new BasePriceCandidate(p, l)).Take(10001).ToArrayAsync(ct);
        if (sources.Length > 10000) return new("limit", ["base_resolution_limit"], null, null);
        foreach (var line in planned)
        {
            var basis = line.BaseResolution!;
            if (!sources.Any(c => c.List.Id == basis.PriceListId && c.List.ListKey == basis.ListKey
                && c.List.Version == basis.Version && c.List.Fingerprint == basis.Fingerprint && c.List.IsActive
                && c.List.Currency == "UAH" && (c.List.LocationId == null || c.List.LocationId == row.StoreId)
                && c.List.ValidFrom <= basis.EvaluatedAt && (c.List.ValidUntil == null || basis.EvaluatedAt < c.List.ValidUntil)
                && c.Line.ItemId == basis.ItemId && c.Line.Price == basis.Price && c.Line.VatRate == basis.VatRate
                && c.Line.Unit == line.Unit)) return new("mismatch", ["base_resolution"], result.InputHash, result.ResultHash);
        }
        return result;
    }
    public async Task<PosPricingChannelReport> ChannelsAsync(DateTimeOffset from, DateTimeOffset until, Guid? locationId, CancellationToken ct)
    {
        if (from.Year is < 2020 or > 2100 || until <= from || until - from > TimeSpan.FromDays(31) || locationId == Guid.Empty)
            throw new ArgumentException("Report requires a half-open period of at most 31 days.");
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'", ct);
        var s = await Scope(db, ct);
        if (locationId is Guid id && !s.CanLocation(id)) throw new WorkAccessDeniedException();
        var start = from.UtcDateTime; var end = until.UtcDateTime;
        // CreatedAt is server acceptance time; offline EvaluatedAt is deliberately a separate clock.
        var sales = db.PosTransactions.AsNoTracking().Where(t => t.TenantId == s.TenantId && t.CreatedAt >= start
            && t.CreatedAt < end && (s.TenantWide || s.LocationIds.Contains(t.StoreId)) && (locationId == null || t.StoreId == locationId));
        var archived = from t in sales join a in db.Set<PosPricingSnapshot>() on t.Id equals a.TransactionId
            where a.TenantId == s.TenantId select new { a.Channel, t.TotalAmount, t.TaxAmount };
        var groups = await archived.GroupBy(a => a.Channel).Select(g => new PosPricingChannel(g.Key, g.Count(),
            g.Sum(a => a.TotalAmount), g.Sum(a => a.TaxAmount))).ToArrayAsync(ct);
        groups = groups.OrderBy(g => g.Channel, StringComparer.Ordinal).ToArray();
        var count = await sales.CountAsync(ct);
        var legacy = await sales.CountAsync(t => !t.PricingArchiveRequired, ct);
        var missing = await sales.CountAsync(t => t.PricingArchiveRequired && !db.Set<PosPricingSnapshot>()
            .Any(a => a.TenantId == s.TenantId && a.TransactionId == t.Id), ct);
        await tx.CommitAsync(ct); return new(start, end, count, groups.Sum(g => g.Count), legacy, missing, groups);
    }
    public async Task<PosPricingDiagnostics> DiagnosticsAsync(DateTimeOffset from, DateTimeOffset until, Guid? locationId, int page, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || from.Year is < 2020 or > 2100 || until <= from
            || until - from > TimeSpan.FromDays(31) || locationId == Guid.Empty) throw new ArgumentException("Invalid diagnostic range.");
        await using var db = new AppDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'", ct);
        var s = await Scope(db, ct);
        if (locationId is Guid id && !s.CanLocation(id)) throw new WorkAccessDeniedException();
        var start = from.UtcDateTime; var end = until.UtcDateTime;
        var q = from t in db.PosTransactions.AsNoTracking()
            join a in db.Set<PosPricingSnapshot>().AsNoTracking() on t.Id equals a.TransactionId
            where t.TenantId == s.TenantId && a.TenantId == s.TenantId && t.CreatedAt >= start && t.CreatedAt < end
                && (s.TenantWide || s.LocationIds.Contains(a.StoreId)) && (locationId == null || a.StoreId == locationId)
            select new { AcceptedAt = t.CreatedAt, Archive = a };
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(r => r.AcceptedAt).ThenBy(r => r.Archive.TransactionId).Skip((page - 1) * 25).Take(25).ToArrayAsync(ct);
        var outcomes = new List<(string Channel, string Outcome)>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            string outcome;
            try { outcome = (await CompareAsync(db, row.Archive, ct)).Outcome; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { outcome = "error"; } // Never omit an unevaluable record from the denominator.
            outcomes.Add((row.Archive.Channel, outcome));
        }
        var totals = outcomes.GroupBy(o => o).OrderBy(g => g.Key.Channel, StringComparer.Ordinal).ThenBy(g => g.Key.Outcome, StringComparer.Ordinal)
            .Select(g => new PosPricingOutcome(g.Key.Channel, g.Key.Outcome, g.Count())).ToArray();
        await tx.CommitAsync(ct);
        return new(page, 25, count, rows.Length, page * 25 < count, totals);
    }
}
