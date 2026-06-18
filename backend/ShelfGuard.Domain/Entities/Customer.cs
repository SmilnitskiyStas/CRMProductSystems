namespace ShelfGuard.Domain.Entities;

/// <summary>
/// CRM customer for POS and general business operations.
/// Tracks purchase history and enables loyalty / VIP workflows.
/// </summary>
public sealed class Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    /// <summary>Free-form tags: "vip", "wholesale", etc.</summary>
    public List<string> Tags { get; set; } = [];
    /// <summary>Denormalized counter — updated by service or trigger.</summary>
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public ICollection<PosTransaction> Transactions { get; init; } = new List<PosTransaction>();
}
