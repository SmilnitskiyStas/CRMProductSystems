namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Per-tenant loyalty program configuration — exactly one row per tenant (unique
/// TenantId). Defaults (3% accrual / 50% redemption cap) are proposed starting values
/// from the plan, not figures sourced from any competitor analysis (which covers
/// analytics only, never bonus mechanics) — tenants are expected to tune these via
/// Settings once the backend/frontend land.
/// </summary>
public sealed class LoyaltyProgramSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }

    public bool IsEnabled { get; set; } = true;
    /// <summary>Percentage of the net (post-redemption) sale amount accrued as bonus, e.g. 3.00 means 3%.</summary>
    public decimal AccrualRatePercent { get; set; } = 3.0m;
    /// <summary>Max % of a sale's total that may be paid with bonus balance, e.g. 50.00 means 50%.</summary>
    public decimal RedemptionCapPercent { get; set; } = 50.0m;
    /// <summary>Balance floor a membership must keep after any redemption.</summary>
    public decimal MinRedemptionBalance { get; set; }
    /// <summary>How long a "live" QR/barcode TOTP code stays valid before rotating.</summary>
    public int CodeTtlSeconds { get; set; } = 30;
    /// <summary>
    /// How this tenant's consumers should render their universal checkout code (TASK-499):
    /// exactly <c>"qr"</c> or <c>"barcode"</c> (Code 128). String-typed rather than a C# enum
    /// since it round-trips through JSON/SQL as a plain value. Chosen per store network
    /// (Tenant), never per individual store.
    /// </summary>
    public string CustomerCodeFormat { get; set; } = "barcode";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
}
