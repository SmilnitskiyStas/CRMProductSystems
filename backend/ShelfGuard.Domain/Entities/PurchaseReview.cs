namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Consumer review of a specific purchase (TASK-613) — mirrors <see cref="SupplierReview"/>'s
/// shape (rating + comment + one staff reply), but keyed to a <see cref="PosTransaction"/>
/// instead of a <see cref="Supplier"/>. Unique constraint on <see cref="PosTransactionId"/>:
/// one review per purchase — confirmed product decision (approved plan §1d), not a
/// defensive guess.
/// </summary>
public sealed class PurchaseReview
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ConsumerAccountId { get; init; }
    /// <summary>The purchase being reviewed. Restrict — a sale is never cascade-deleted by a review.</summary>
    public Guid PosTransactionId { get; init; }
    /// <summary>Rating 1–5.</summary>
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Staff's reply to this review (self-service, one reply per review).</summary>
    public string? ReplyText { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
    public Guid? RepliedByUserId { get; set; }

    public Tenant? Tenant { get; init; }
    public ConsumerAccount? ConsumerAccount { get; init; }
    public PosTransaction? PosTransaction { get; init; }
    public User? RepliedByUser { get; init; }
}
