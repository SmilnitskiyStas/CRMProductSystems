using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Buyer-side of per-employee supplier ratings (TASK-695, Phase 8). Two rating paths:
///   • <see cref="RateOrderManagerAsync"/> — after a delivered order, rate the responsible
///     manager (<c>MarketplaceOrder.ConfirmedByUserId</c>). One rating per order.
///   • <see cref="RateChatParticipantAsync"/> — from the shared chat thread, rate a supplier
///     staff member who replied in it. One rating per (employee, session).
/// Both are upserts (a second call updates the existing rating). Ratings are supplier-internal:
/// NOT shown on the public supplier profile and NOT rolled into <c>SupplierMetrics.Rating</c>.
/// </summary>
public interface ISupplierEmployeeReviewService
{
    /// <summary>
    /// Upsert the calling buyer's rating of the manager responsible for <paramref name="orderId"/>.
    /// Requires the order to belong to the caller, be <c>delivered</c>, and have a
    /// <c>ConfirmedByUserId</c>.
    /// </summary>
    Task<(SupplierEmployeeReviewDto? Review, string? Error)> RateOrderManagerAsync(
        Guid clientTenantId, Guid orderId, RateSupplierEmployeeDto request, Guid ratedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert the calling buyer's rating of a supplier staff member who replied in the shared
    /// chat thread with <paramref name="supplierId"/>. Requires an existing chat session for the
    /// pair AND that <c>request.SupplierUserId</c> actually sent ≥1 message in it from the
    /// supplier side.
    /// </summary>
    Task<(SupplierEmployeeReviewDto? Review, string? Error)> RateChatParticipantAsync(
        Guid clientTenantId, Guid supplierId, RateChatParticipantDto request, Guid ratedByUserId,
        CancellationToken ct = default);

    /// <summary>The calling buyer's rating for one order, or null if not rated yet.</summary>
    Task<SupplierEmployeeReviewDto?> GetOrderManagerRatingAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Every chat-path rating the calling buyer left in its thread with <paramref name="supplierId"/>
    /// — empty when there is no thread yet.
    /// </summary>
    Task<IReadOnlyList<SupplierEmployeeReviewDto>> GetMyChatParticipantRatingsAsync(
        Guid clientTenantId, Guid supplierId, CancellationToken ct = default);
}
