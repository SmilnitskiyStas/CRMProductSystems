using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ICannibalizationRepository
{
    Task<Discount?> GetDiscountAsync(Guid discountId, CancellationToken ct = default);

    /// <summary>The catalog product the discount targets (for segment lookup).</summary>
    Task<Item?> GetDiscountProductAsync(Guid discountId, CancellationToken ct = default);

    Task<List<PromoCannibalization>> GetByDiscountAsync(Guid discountId, CancellationToken ct = default);

    Task<PromoCannibalization?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Active products sharing the segment, excluding the discounted one.</summary>
    Task<List<Item>> GetSegmentSiblingsAsync(
        Guid segmentId, Guid excludeProductId, CancellationToken ct = default);

    /// <summary>
    /// productId → multiplied coefficient of APPLIED rows whose discount is active now
    /// for the store. Used by the order formula.
    /// </summary>
    Task<Dictionary<Guid, decimal>> GetActivePromoCoefficientsAsync(
        Guid storeId, DateTime now, CancellationToken ct = default);

    Task AddAsync(PromoCannibalization row, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
