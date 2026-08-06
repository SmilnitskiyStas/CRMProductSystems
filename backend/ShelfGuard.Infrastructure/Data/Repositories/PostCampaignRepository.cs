using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// See <see cref="IPostCampaignRepository"/> for the full design context. Unlike the raw-SQL
/// repositories in sibling Фаза 1-3 modules, every method here is plain EF Core LINQ — this
/// feature's aggregates (purchase_count/turnover/last_purchase_date per customer) have a
/// straightforward LINQ translation, unlike Фаза 1/2's NTILE/PERCENTILE_CONT quintile math, which
/// has none. Flagged explicitly in TASK-472's task log: zero new raw-SQL string-interpolation
/// surface added by this file.
/// </summary>
public sealed class PostCampaignRepository : IPostCampaignRepository
{
    private const string ExcludedStatus = "fiscalization_failed";

    private readonly AppDbContext _db;

    public PostCampaignRepository(AppDbContext db) => _db = db;

    public Task<PostCampaignSegment?> GetSegmentAsync(Guid tenantId, Guid segmentId, CancellationToken ct = default) =>
        _db.PostCampaignSegments.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == segmentId, ct);

    public async Task<IReadOnlyList<PostCampaignSegment>> ListSegmentsAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.PostCampaignSegments
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task AddSegmentAsync(PostCampaignSegment segment, CancellationToken ct = default) =>
        await _db.PostCampaignSegments.AddAsync(segment, ct);

    public async Task AddMembersAsync(IReadOnlyList<PostCampaignSegmentMember> members, CancellationToken ct = default)
    {
        if (members.Count == 0) return;
        await _db.PostCampaignSegmentMembers.AddRangeAsync(members, ct);
    }

    public async Task<IReadOnlyList<Guid>> GetMemberCustomerIdsAsync(Guid tenantId, Guid segmentId, CancellationToken ct = default) =>
        await _db.PostCampaignSegmentMembers
            .Where(m => m.TenantId == tenantId && m.SegmentId == segmentId)
            .Select(m => m.CustomerId)
            .ToListAsync(ct);

    /// <summary>
    /// TASK-478 fix (bug writeup: bug-task476-phone-import-matching-format-mismatch): the ID and
    /// phone candidates are resolved as two SEPARATE passes rather than one combined
    /// <c>Where</c>, because the phone pass needs a client-side (C#) normalization step that has
    /// no SQL translation — see below. Split into two round trips instead of one, still bounded
    /// and cheap for this feature's realistic scale (SME tenants, request-scoped by
    /// <see cref="PostCampaignService.MaxAcceptedRows"/>).
    /// </summary>
    public async Task<IReadOnlyList<MatchedCustomerRow>> FindCustomersByIdsOrPhonesAsync(
        Guid tenantId, IReadOnlyList<Guid> candidateIds, IReadOnlyList<string> candidatePhones, CancellationToken ct = default)
    {
        if (candidateIds.Count == 0 && candidatePhones.Count == 0) return [];

        // ── Pass 1: GUID candidates — unchanged exact-match query. Same list.Contains(x) ->
        // Postgres `= ANY(@p)` translation already established at
        // MarketingAnalyticsRepository.GetExportCustomersAsync — an empty candidate list simply
        // never matches (Postgres `x = ANY('{}')` is always false), no cardinality guard needed.
        var byId = candidateIds.Count == 0
            ? []
            : await _db.Customers
                .Where(c => c.TenantId == tenantId && candidateIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Phone })
                .ToListAsync(ct);

        // Normalized here too (not just the phone pass below) so MatchedCustomerRow.Phone always
        // means "this customer's normalized phone, if any" regardless of which pass matched them
        // — otherwise a customer submitted as BOTH a GUID and their own (non-canonical-format)
        // phone in the same import would resolve via the GUID but the phone token would still
        // wrongly read back as "unknown", since PostCampaignService.ImportAsync's own byPhone
        // dictionary is keyed off this field and looked up with the always-normalized
        // token.NormalizedPhone.
        var result = byId
            .Select(c => new MatchedCustomerRow(c.Id, PhoneNormalizer.Normalize(c.Phone)))
            .ToList();

        if (candidatePhones.Count == 0) return result;

        // ── Pass 2: phone candidates. `candidatePhones` always arrives pre-normalized to
        // +380XXXXXXXXX (SegmentImportParser.Classify, via this same PhoneNormalizer) — but
        // Customer.Phone is stored in whatever format the marketer typed at CRM-entry time
        // (CustomerService's own PhoneRegex is intentionally permissive and only ever .Trim()s
        // the value; AutoServiceService doesn't even trim). A raw string-equality WHERE against
        // the stored column — the previous behavior — therefore only ever matched a customer
        // whose Phone happened to already be in the exact canonical form, silently missing every
        // other equally-valid stored format (e.g. "0501234567", "380-50-111-00-11"). Fixed by
        // normalizing the STORED side too, client-side in C#, via the IDENTICAL
        // PhoneNormalizer.Normalize the import parser itself uses — zero risk of a second,
        // drifting implementation. A SQL-side regex/computed-column predicate was deliberately
        // avoided per this task's brief.
        //
        // Excludes rows already resolved in Pass 1 (`!candidateIds.Contains(c.Id)`) so a customer
        // submitted via both a GUID and a phone token can never produce two MatchedCustomerRow
        // entries for the same Id — PostCampaignService.ImportAsync's `found.ToDictionary(c =>
        // c.Id)` would throw on a duplicate key otherwise.
        //
        // Narrow projection (id + phone only) keeps this a single bounded round trip even though
        // it loads every non-null-phone Customer row for the tenant — reasonable for this
        // system's actual target market (SME retail/auto-service tenants, not a mass-consumer
        // platform) and bounded per-import by the same MaxAcceptedRows/request-scoped nature of
        // the import operation. A cheaper SQL-side pre-filter (e.g. an indexed EndsWith on the
        // last few significant digits) is a possible future optimization if a tenant's customer
        // count ever makes this pass costly — not applied here since correctness via the real
        // normalizer, not a heuristic substitute, is what this fix is actually for.
        var phoneCandidateSet = candidatePhones.ToHashSet(StringComparer.Ordinal);

        var phoneScanRows = await _db.Customers
            .Where(c => c.TenantId == tenantId && c.Phone != null && !candidateIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Phone })
            .ToListAsync(ct);

        foreach (var row in phoneScanRows)
        {
            var normalized = PhoneNormalizer.Normalize(row.Phone);
            if (normalized is not null && phoneCandidateSet.Contains(normalized))
                result.Add(new MatchedCustomerRow(row.Id, normalized));
        }

        return result;
    }

    public async Task<IReadOnlyList<CustomerNameRow>> GetCustomerNamesAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return [];

        return await _db.Customers
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .Select(c => new CustomerNameRow(c.Id, c.Name, c.Phone))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CustomerPeriodMetricsRow>> GetCustomerPeriodMetricsAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return [];

        var raw = await FetchRawLinesAsync(tenantId, customerIds, storeIds, fromUtc, toUtc, ct);

        // Grouped/aggregated in-memory (LINQ-to-Objects), not via a server-side GroupBy — same
        // deliberate precedent AnalyticsRepository's own date-bucketed queries already follow in
        // this codebase, rather than relying on Npgsql's GroupBy-to-SQL translation. The matched-
        // customer population is bounded by the 20,000-row import cap and each window only ever
        // covers a single campaign-sized date range, so this is a small, bounded in-memory pass.
        return raw
            .GroupBy(t => t.CustomerId)
            .Select(g => new CustomerPeriodMetricsRow(g.Key, g.Count(), g.Sum(t => t.TotalAmount), g.Max(t => t.CreatedAt)))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetDailyTurnoverByOrdinalDayAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly windowStart, DateOnly windowEnd, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return new Dictionary<int, decimal>();

        var fromUtc = windowStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = windowEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var raw = await FetchRawLinesAsync(tenantId, customerIds, storeIds, fromUtc, toUtc, ct);

        return raw
            .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt).DayNumber - windowStart.DayNumber)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.TotalAmount));
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private sealed record RawTransactionLine(Guid CustomerId, decimal TotalAmount, DateTime CreatedAt);

    /// <summary>Single shared fetch: WHERE + SELECT only, no GroupBy pushed to SQL — a plain,
    /// unambiguous translation. Both callers above aggregate the (small, bounded) result in
    /// memory rather than relying on GroupBy-to-SQL translation for a <c>.Date</c>-derived or
    /// nullable-unwrapped grouping key.</summary>
    private async Task<List<RawTransactionLine>> FetchRawLinesAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var query = _db.PosTransactions.Where(t =>
            t.TenantId == tenantId &&
            t.CustomerId != null && customerIds.Contains(t.CustomerId!.Value) &&
            t.Status != ExcludedStatus &&
            t.CreatedAt >= fromUtc && t.CreatedAt <= toUtc);

        if (storeIds is { Count: > 0 })
            query = query.Where(t => storeIds.Contains(t.StoreId));

        return await query
            .Select(t => new RawTransactionLine(t.CustomerId!.Value, t.TotalAmount, t.CreatedAt))
            .ToListAsync(ct);
    }
}
