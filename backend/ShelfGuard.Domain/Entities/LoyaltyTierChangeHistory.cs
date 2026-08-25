namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Append-only audit trail of a <see cref="LoyaltyMembership"/> moving between
/// <see cref="LoyaltyTierDefinition"/> rungs (TASK-613), written by the nightly
/// tier-recompute worker job whenever a membership's qualifying tier changes. Every
/// property is <c>init</c>-only, mirroring <see cref="LoyaltyLedgerEntry"/>'s
/// immutability discipline — rows are never updated or deleted.
/// </summary>
public sealed class LoyaltyTierChangeHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid MembershipId { get; init; }

    /// <summary>Null when the membership had no qualifying tier yet (first-ever assignment).</summary>
    public Guid? FromTierId { get; init; }
    /// <summary>Null when the membership dropped below the lowest tier's threshold.</summary>
    public Guid? ToTierId { get; init; }
    public decimal FromScore { get; init; }
    public decimal ToScore { get; init; }
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;

    public LoyaltyMembership? Membership { get; init; }
    public LoyaltyTierDefinition? FromTier { get; init; }
    public LoyaltyTierDefinition? ToTier { get; init; }
}
