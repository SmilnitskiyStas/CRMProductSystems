namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Per-tenant loyalty tier ladder rung (TASK-613) — e.g. Bronze/Silver/Gold. Tiers are
/// ranked by <see cref="SortOrder"/> (ascending) and a membership's
/// <see cref="LoyaltyMembership.CompositeScore"/> is compared against
/// <see cref="MinCompositeScore"/> to determine the highest rung it currently qualifies
/// for. Reaching a tier grants immediate functional benefits —
/// <see cref="AccrualMultiplier"/> scales bonus accrual and <see cref="DiscountPercent"/>
/// is applied at checkout — not just a cosmetic badge (per the approved plan's "Узгоджені
/// рішення"). Recomputed periodically by the nightly tier-recompute worker job, not live
/// at request time, so a membership's <see cref="LoyaltyMembership.CurrentTierId"/> can
/// lag its true qualifying tier by up to one recompute cycle.
/// </summary>
public sealed class LoyaltyTierDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }

    public string Name { get; set; } = string.Empty;
    /// <summary>Retailer-authored explanation shown to consumers on the rank screen.</summary>
    public string? Description { get; set; }
    /// <summary>Optional tenant-owned rank artwork uploaded by an enterprise administrator.</summary>
    public string? ImageUrl { get; set; }
    /// <summary>Ascending rank within the tenant's ladder — unique per (TenantId, SortOrder).</summary>
    public int SortOrder { get; set; }
    /// <summary>Minimum composite RFM-like score required to hold this tier.</summary>
    public decimal MinCompositeScore { get; set; }
    /// <summary>Multiplier applied to bonus accrual while a membership holds this tier (1.0 = no change).</summary>
    public decimal AccrualMultiplier { get; set; } = 1.0m;
    /// <summary>Automatic checkout discount percent granted while a membership holds this tier.</summary>
    public decimal DiscountPercent { get; set; } = 0m;

    // Explicit, independently selectable promotion requirements. Null means the retailer did
    // not select that metric; every non-null requirement must be satisfied. The legacy
    // MinCompositeScore remains available for existing ladders and as an optional RFM metric.
    public bool RequireCompletedProfile { get; set; }
    public int? MinMembershipDays { get; set; }
    public decimal? MinEarnedBonuses { get; set; }
    public decimal? MinCashSpend { get; set; }
    public decimal? MinBonusSpend { get; set; }
    public int? MinPurchaseCount { get; set; }
    public int? MinReviewCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
}
