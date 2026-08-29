namespace ShelfGuard.Domain.Entities;

/// <summary>One expirable FIFO portion of earned or rewarded bonuses.</summary>
public sealed class LoyaltyBonusLot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid MembershipId { get; init; }
    public Guid SourceLedgerEntryId { get; init; }
    public decimal OriginalAmount { get; init; }
    public decimal RemainingAmount { get; set; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public LoyaltyMembership? Membership { get; init; }
    public LoyaltyLedgerEntry? SourceLedgerEntry { get; init; }
}
