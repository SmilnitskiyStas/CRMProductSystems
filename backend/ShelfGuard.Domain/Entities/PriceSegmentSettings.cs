namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Per-tenant configuration for Фаза 2 marketing analytics (price segments + frequency
/// decline/reactivation) — exactly one row per tenant (unique TenantId), same shape as
/// LoyaltyProgramSettings (TASK-404). Staff-only configuration: unlike
/// loyalty_memberships/loyalty_ledger_entries, there is no consumer access path to this
/// table at all, so it carries only the canonical RLS triad (see AddPriceSegmentSettings
/// migration) — no identity-based policy.
/// </summary>
public sealed class PriceSegmentSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }

    /// <summary>
    /// Default "frequency decline, % or more" threshold surfaced on the Фаза 2 frequency
    /// tab's slider. A proposed starting value, not a figure sourced from the competitor
    /// analysis (which never disclosed exact thresholds) — tenants can override on request.
    /// </summary>
    public decimal DefaultFrequencyDeclineThresholdPercent { get; set; } = 30.0m;

    /// <summary>
    /// Optional minimum receipt count a customer must have to count toward the price-segment
    /// quantile boundary calculation (statistical reliability guard against a handful of
    /// customers skewing P20/P40/P60/P80/P90/P97 tiers). Null means no threshold is applied —
    /// the v1 default; the boundary calculation does not yet consult this field.
    /// </summary>
    public int? MinReceiptsForBoundaries { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
}
