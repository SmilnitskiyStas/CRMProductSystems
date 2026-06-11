namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Order-coefficient adjustment caused by a promo (v2-spec §5).
/// The discounted product sells more (k ≈ 2.0); same-segment neighbours lose
/// demand to it (k ≈ 0.7). Suggestions are generated automatically and take
/// effect in the order formula only after the manager applies them.
/// </summary>
public sealed class PromoCannibalization
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DiscountId { get; init; }
    public Guid AffectedProductId { get; init; }
    public decimal OrderCoefficient { get; set; }
    /// <summary>manual / ai_suggested / learned</summary>
    public string Source { get; set; } = "ai_suggested";
    /// <summary>Not in spec schema — needed because spec flow has an explicit apply step.</summary>
    public bool IsApplied { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Discount? Discount { get; init; }
    public CatalogProduct? AffectedProduct { get; init; }
}
