namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A <see cref="ConsumerAccount"/>'s enrollment in one tenant's loyalty/bonus program —
/// exactly one row per (tenant, consumer), see the unique index in AppDbContext. Balance
/// is a denormalized running total (same pattern as <see cref="Customer.TotalSpent"/>);
/// the authoritative append-only audit trail lives in <see cref="LoyaltyLedgerEntry"/>.
/// <see cref="CustomerId"/> links to the tenant's own CRM customer record — auto-found by
/// phone or auto-created at membership creation time (service-layer concern, not here).
/// <see cref="LinkedUserId"/> is set only for the "staff joins their own employer's
/// program" case (plan §"Кейс 2") and is otherwise null.
/// The "live" QR/barcode is backed by <see cref="TotpSecret"/> — reuses the same TOTP
/// infrastructure as User 2FA (Otp.NET / ITotpService), never leaves the server.
/// <see cref="LastRedeemedTimestep"/> is the anti-replay high-water mark, same shape as
/// <see cref="User.TotpLastTimestep"/>.
/// </summary>
public sealed class LoyaltyMembership
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Short, database-generated numeric loyalty-card number.</summary>
    public long CardNumber { get; private set; }
    public Guid TenantId { get; init; }

    public Guid ConsumerAccountId { get; init; }
    /// <summary>Auto-linked/auto-created CRM customer within this tenant. Nullable/SetNull.</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>Staff member's own User row, when this membership is their "join my employer's program" case. Nullable/SetNull.</summary>
    public Guid? LinkedUserId { get; set; }
    /// <summary>
    /// TASK-507: which store within this already-joined network the consumer primarily shops
    /// at — an additional, optional preference, NOT a change to membership/join semantics
    /// (membership stays exactly one per (tenant, consumer), never per store; see class doc).
    /// Set via <see cref="LoyaltyService"/>'s dedicated preferred-store endpoint, not join.
    /// Nullable/SetNull, same convention as <see cref="CustomerId"/>/<see cref="LinkedUserId"/>.
    /// </summary>
    public Guid? PreferredStoreId { get; set; }

    /// <summary>Base32 TOTP secret backing the "live" QR/barcode. Never leaves the server.</summary>
    public string TotpSecret { get; set; } = string.Empty;
    /// <summary>Anti-replay high-water mark for `POST /api/loyalty/resolve-code`.</summary>
    public long? LastRedeemedTimestep { get; set; }

    public decimal Balance { get; set; }
    /// <summary>active | blocked | left</summary>
    public string Status { get; set; } = "active";
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// TASK-613: the highest <see cref="LoyaltyTierDefinition"/> this membership currently
    /// qualifies for, normally recomputed by the nightly tier-recompute worker job. Null until
    /// the first recompute run, or when the membership doesn't clear even the lowest tier's
    /// threshold. Nullable/SetNull — a tenant can delete a tier definition without dragging
    /// memberships down with it. TASK-627 exception: also set once, directly, at membership
    /// creation time (<c>LoyaltyService.CreateMembershipCoreAsync</c>/<c>JoinAsStaffAsync</c>)
    /// to the ladder's entry tier, so a brand-new member isn't tierless for up to 24h until the
    /// next 04:00 run — safe because that write is part of the row's own INSERT, with no
    /// existing row and no concurrent writer to race (see <see cref="CompositeScore"/>).
    /// </summary>
    public Guid? CurrentTierId { get; set; }
    /// <summary>
    /// RFM-like composite score computed by the nightly tier-recompute worker job (plan
    /// §3), independent of <see cref="Balance"/> — never written by <c>PosService</c> or any
    /// other request-time code path that UPDATEs an existing row, only by that job, to avoid
    /// conflicting with the concurrency token (xmin) PosService/LoyaltyService use for Balance
    /// updates. TASK-627 exception: the one request-time writer is membership creation itself
    /// (see <see cref="CurrentTierId"/>), which sets this to 0 as part of the row's own INSERT
    /// — no existing row is ever touched, so there is nothing for it to race against.
    /// </summary>
    public decimal CompositeScore { get; set; }
    /// <summary>When <see cref="CompositeScore"/>/<see cref="CurrentTierId"/> were last recomputed. Null until the first run.</summary>
    public DateTimeOffset? TierScoreUpdatedAt { get; set; }
    public bool TierProfileCompleted { get; set; }
    public int TierMembershipDays { get; set; }
    public decimal TierEarnedBonuses { get; set; }
    public decimal TierCashSpend { get; set; }
    public decimal TierBonusSpend { get; set; }
    public int TierPurchaseCount { get; set; }
    public int TierReviewCount { get; set; }

    public Tenant? Tenant { get; init; }
    public ConsumerAccount? ConsumerAccount { get; init; }
    public Customer? Customer { get; init; }
    public User? LinkedUser { get; init; }
    public LoyaltyTierDefinition? CurrentTier { get; init; }
    public ICollection<LoyaltyLedgerEntry> LedgerEntries { get; init; } = new List<LoyaltyLedgerEntry>();
}

/// <summary>Valid <see cref="LoyaltyMembership.Status"/> values.</summary>
public static class LoyaltyMembershipStatus
{
    public const string Active  = "active";
    public const string Blocked = "blocked";
    /// <summary>
    /// TASK-548: consumer left the network via <c>DELETE /api/v1/retailers/{slug}/membership</c>
    /// (<see cref="Loyalty.LoyaltyService.LeaveAsync"/>). A soft deactivation, not a delete —
    /// Balance/JoinedAt/LedgerEntries/TotpSecret are all preserved unchanged (same
    /// never-hard-delete-financial-history precedent as <see cref="Customer.TotalSpent"/> and
    /// <see cref="Tenant.Deactivate"/>). Rejoining the same network
    /// (<see cref="Loyalty.LoyaltyService.JoinAsync"/>) reactivates this same row back to
    /// <see cref="Active"/> rather than creating a new membership or erroring.
    /// </summary>
    public const string Left = "left";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Active, Blocked, Left };
}
