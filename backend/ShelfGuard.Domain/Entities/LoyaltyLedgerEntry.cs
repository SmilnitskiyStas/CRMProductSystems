namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Append-only bonus ledger entry — the audit trail behind
/// <see cref="LoyaltyMembership.Balance"/>. Rows are never updated or deleted; the running
/// balance only ever moves forward by inserting a new row and updating the parent
/// membership's denormalized <see cref="LoyaltyMembership.Balance"/> in the same
/// transaction (mirrors <c>ProductStock</c>'s movement-log discipline). Every property is
/// <c>init</c>-only to reflect that immutability at the type level.
/// </summary>
public sealed class LoyaltyLedgerEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid MembershipId { get; init; }

    /// <summary>accrual | redemption | manual_adjustment | expiry</summary>
    public string EntryType { get; init; } = string.Empty;
    /// <summary>Signed: positive for accrual, negative for redemption/expiry.</summary>
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }

    /// <summary>The sale this entry was earned/redeemed against, if any (manual_adjustment has none). Nullable/SetNull.</summary>
    public Guid? PosTransactionId { get; init; }
    /// <summary>Staff member who performed a manual_adjustment; null for POS-driven entries. Nullable/SetNull.</summary>
    public Guid? CreatedByUserId { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public LoyaltyMembership? Membership { get; init; }
    public PosTransaction? PosTransaction { get; init; }
    public User? CreatedByUser { get; init; }
}

/// <summary>Valid <see cref="LoyaltyLedgerEntry.EntryType"/> values.</summary>
public static class LoyaltyEntryType
{
    public const string Accrual         = "accrual";
    public const string Redemption      = "redemption";
    public const string ManualAdjustment = "manual_adjustment";
    public const string Expiry          = "expiry";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Accrual, Redemption, ManualAdjustment, Expiry };
}
