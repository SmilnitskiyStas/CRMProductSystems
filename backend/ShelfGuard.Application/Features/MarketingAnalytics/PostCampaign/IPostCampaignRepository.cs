using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

// ── Raw rows returned by the repository — the SERVICE maps these into wire-facing Dtos, same
// Application/Infrastructure separation every other MarketingAnalytics repository already
// establishes (IMarketingAnalyticsRepository, IPriceSegmentsRepository, IAudienceBuilderRepository). ─

/// <summary>One customer resolved from the bulk id-or-phone lookup at import time.</summary>
public sealed record MatchedCustomerRow(Guid Id, string? Phone);

public sealed record CustomerNameRow(Guid CustomerId, string Name, string? Phone);

/// <summary><see cref="LastPurchaseDate"/> is only ever the max <c>CreatedAt</c> WITHIN the
/// queried window — a row only exists for a customer with at least one purchase in that window
/// (a customer with zero is simply absent from the result, never a zero-valued row).</summary>
public sealed record CustomerPeriodMetricsRow(Guid CustomerId, int PurchaseCount, decimal Turnover, DateTime LastPurchaseDate);

/// <summary>
/// Data access for Фаза 4 post-campaign segments (TASK-472). Unlike the raw-SQL repositories in
/// sibling Фаза 1-3 modules (NTILE/PERCENTILE_CONT/UNNEST term-matching have no LINQ equivalent),
/// every method here is a straightforward EF Core LINQ query against <c>PostCampaignSegment(s)</c>/
/// <c>Customer</c>/<c>PosTransaction</c> — no raw SQL, flagged explicitly in this task's log for
/// the next security-reviewer pass since it means zero new raw-SQL string-interpolation surface.
/// Bulk-ID/phone filtering uses the same <c>list.Contains(x)</c> → Postgres <c>= ANY(@p)</c>
/// translation already established at <c>MarketingAnalyticsRepository.GetExportCustomersAsync</c>.
/// </summary>
public interface IPostCampaignRepository
{
    Task<PostCampaignSegment?> GetSegmentAsync(Guid tenantId, Guid segmentId, CancellationToken ct = default);

    Task<IReadOnlyList<PostCampaignSegment>> ListSegmentsAsync(Guid tenantId, CancellationToken ct = default);

    Task AddSegmentAsync(PostCampaignSegment segment, CancellationToken ct = default);

    Task AddMembersAsync(IReadOnlyList<PostCampaignSegmentMember> members, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetMemberCustomerIdsAsync(Guid tenantId, Guid segmentId, CancellationToken ct = default);

    /// <summary>Bulk-resolves import tokens against real Customer rows — by
    /// <see cref="Domain.Entities.Customer.Id"/> for GUID tokens (one query), by
    /// <see cref="Domain.Entities.Customer.Phone"/> for phone tokens (a second query; TASK-478
    /// fix — see the implementation's own remarks). <paramref name="candidatePhones"/> always
    /// arrives already normalized to +380XXXXXXXXX; the stored <c>Customer.Phone</c> column is
    /// normalized CLIENT-SIDE (same <c>PhoneNormalizer.Normalize</c> the import parser itself
    /// uses) before comparison, so a match no longer depends on the customer's phone happening to
    /// already be stored in canonical form — every returned row's <see cref="MatchedCustomerRow.Phone"/>
    /// is that normalized form (or null), regardless of which pass matched it.</summary>
    Task<IReadOnlyList<MatchedCustomerRow>> FindCustomersByIdsOrPhonesAsync(
        Guid tenantId, IReadOnlyList<Guid> candidateIds, IReadOnlyList<string> candidatePhones, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerNameRow>> GetCustomerNamesAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, CancellationToken ct = default);

    /// <summary>Per-customer purchase_count/turnover/last_purchase_date within [fromUtc,toUtc]
    /// (+ optional store filter) — one row per <c>PosTransaction</c> IS one receipt already (no
    /// join to line items needed), so purchase_count is a plain COUNT, never
    /// COUNT(DISTINCT...) over a fanned-out join. Excludes <c>Status = 'fiscalization_failed'</c>,
    /// same convention every other MarketingAnalytics repository already applies.</summary>
    Task<IReadOnlyList<CustomerPeriodMetricsRow>> GetCustomerPeriodMetricsAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Turnover summed per ORDINAL day-in-window (key = days since
    /// <paramref name="windowStart"/>, 0-based) — never per calendar date, so the caller can align
    /// two windows of different real dates but identical length (source doc §15).</summary>
    Task<IReadOnlyDictionary<int, decimal>> GetDailyTurnoverByOrdinalDayAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly windowStart, DateOnly windowEnd, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
