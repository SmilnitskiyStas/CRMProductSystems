using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Application.Features.SupplierAnalytics.Dtos;

/// <summary>
/// Supplier team performance over a window (TASK-695, Phase 8) — one row per staff user, with
/// order-throughput, timing, delivery-quality, chat-responsiveness and buyer-rating KPIs.
/// Supplier-internal: the buyer ratings feeding <see cref="SupplierEmployeePerformanceDto.AvgBuyerRating"/>
/// are NOT the public company rating.
/// </summary>
/// <param name="From">Start of the resolved window (inclusive) — may differ from the request when capped at 366 days.</param>
/// <param name="To">End of the resolved window (inclusive).</param>
/// <param name="Employees">One row per current staff member, ordered by name.</param>
public sealed record SupplierTeamPerformanceDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SupplierEmployeePerformanceDto> Employees);

/// <summary>
/// One staff member's KPIs for the window. Rates are fractions in <c>[0, 1]</c>; hour figures are
/// means/medians in decimal hours. A figure is <c>null</c> when its denominator is zero (no data
/// to compute it from) — distinct from <c>0</c>.
/// </summary>
/// <param name="UserId">Supplier-tenant user id.</param>
/// <param name="UserName">Display name.</param>
/// <param name="OrdersConfirmed">Orders this user confirmed whose <c>CreatedAt</c> falls in the window.</param>
/// <param name="OrdersShipped">Orders this user shipped whose <c>ShippedAt</c> falls in the window.</param>
/// <param name="AvgHoursToConfirm">Mean <c>ConfirmedAt − CreatedAt</c> over their confirmed orders (null if none have a <c>ConfirmedAt</c>).</param>
/// <param name="AvgHoursToShip">Mean <c>ShippedAt − ConfirmedAt</c> over their shipped orders that also have a <c>ConfirmedAt</c> (null if none).</param>
/// <param name="OnTimeDeliveryRate">Of their shipped-and-delivered orders with an <c>ExpectedDeliveryDate</c>, the fraction delivered on or before it (null if that denominator is 0).</param>
/// <param name="DiscrepancyFreeRate">Of their shipped orders with a finalized receipt, the fraction whose receipt had no discrepancy note (null if that denominator is 0).</param>
/// <param name="ChatMessagesSent">Chat messages this user sent (supplier side) in the window.</param>
/// <param name="ChatSessionsHandled">Distinct chat sessions they sent in, in the window.</param>
/// <param name="MedianFirstResponseHours">Per session they replied in, the gap from the preceding client message to their first reply — median across those sessions (null if none).</param>
/// <param name="AvgBuyerRating">Mean of the buyer ratings of this user created in the window (null if none).</param>
/// <param name="BuyerReviewCount">Count of those buyer ratings.</param>
/// <param name="OrdersShippedDelta"><c>OrdersShipped</c> vs the equal-length preceding window.</param>
/// <param name="OnTimeDeliveryRateDelta"><c>OnTimeDeliveryRate</c> (null → 0) vs the preceding window.</param>
/// <param name="AvgBuyerRatingDelta"><c>AvgBuyerRating</c> (null → 0) vs the preceding window.</param>
public sealed record SupplierEmployeePerformanceDto(
    Guid UserId,
    string UserName,
    int OrdersConfirmed,
    int OrdersShipped,
    double? AvgHoursToConfirm,
    double? AvgHoursToShip,
    double? OnTimeDeliveryRate,
    double? DiscrepancyFreeRate,
    int ChatMessagesSent,
    int ChatSessionsHandled,
    double? MedianFirstResponseHours,
    double? AvgBuyerRating,
    int BuyerReviewCount,
    PeriodMetricDto OrdersShippedDelta,
    PeriodMetricDto OnTimeDeliveryRateDelta,
    PeriodMetricDto AvgBuyerRatingDelta);
