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
    public Guid TenantId { get; init; }

    public Guid ConsumerAccountId { get; init; }
    /// <summary>Auto-linked/auto-created CRM customer within this tenant. Nullable/SetNull.</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>Staff member's own User row, when this membership is their "join my employer's program" case. Nullable/SetNull.</summary>
    public Guid? LinkedUserId { get; set; }

    /// <summary>Base32 TOTP secret backing the "live" QR/barcode. Never leaves the server.</summary>
    public string TotpSecret { get; set; } = string.Empty;
    /// <summary>Anti-replay high-water mark for `POST /api/loyalty/resolve-code`.</summary>
    public long? LastRedeemedTimestep { get; set; }

    public decimal Balance { get; set; }
    /// <summary>active | blocked</summary>
    public string Status { get; set; } = "active";
    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public ConsumerAccount? ConsumerAccount { get; init; }
    public Customer? Customer { get; init; }
    public User? LinkedUser { get; init; }
    public ICollection<LoyaltyLedgerEntry> LedgerEntries { get; init; } = new List<LoyaltyLedgerEntry>();
}

/// <summary>Valid <see cref="LoyaltyMembership.Status"/> values.</summary>
public static class LoyaltyMembershipStatus
{
    public const string Active  = "active";
    public const string Blocked = "blocked";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Active, Blocked };
}
