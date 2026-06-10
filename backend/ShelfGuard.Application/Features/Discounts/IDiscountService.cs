using ShelfGuard.Application.Features.Discounts.Dtos;

namespace ShelfGuard.Application.Features.Discounts;

public interface IDiscountService
{
    Task<IReadOnlyList<DiscountDto>> GetAllAsync(
        Guid tenantId,
        DiscountListQuery query,
        CancellationToken ct = default);

    Task<(DiscountDto? Discount, string? Error)> GetByIdAsync(
        Guid tenantId, Guid discountId,
        CancellationToken ct = default);

    Task<(DiscountDto? Discount, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy,
        CreateDiscountRequest request,
        CancellationToken ct = default);

    /// <summary>Approves a pending discount (store_manager+).</summary>
    Task<(DiscountDto? Discount, string? Error)> ApproveAsync(
        Guid tenantId, Guid discountId, Guid approvedBy,
        CancellationToken ct = default);

    /// <summary>Cancels a pending or active discount.</summary>
    Task<(DiscountDto? Discount, string? Error)> CancelAsync(
        Guid tenantId, Guid discountId,
        CancellationToken ct = default);
}
