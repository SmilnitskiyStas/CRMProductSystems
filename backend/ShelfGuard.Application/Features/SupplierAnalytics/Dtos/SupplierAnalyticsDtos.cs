using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Application.Features.SupplierAnalytics.Dtos;

/// <summary>
/// Supplier demand analytics over the supplier's own marketplace order history
/// (supplier-portal expansion #7, Phase 6b). Read-only. NO cross-buyer leakage — every figure is
/// derived only from orders where <c>marketplace_order_items.SupplierTenantId == me</c> and the
/// order is not cancelled.
/// </summary>
/// <param name="From">Start of the resolved window (inclusive) — may differ from the request when the range was capped at 366 days.</param>
/// <param name="To">End of the resolved window (inclusive).</param>
/// <param name="TotalRevenue">Σ <c>LineTotal</c> across the window.</param>
/// <param name="OrderCount">Distinct non-cancelled orders in the window.</param>
/// <param name="ItemsSold">Σ <c>Qty</c> across the window.</param>
/// <param name="RevenueDelta">Revenue vs the equal-length window immediately before <see cref="From"/> (TASK-336 <c>PeriodMetricDto</c>).</param>
/// <param name="OrderCountDelta">Order count vs the preceding equal-length window.</param>
/// <param name="ItemsSoldDelta">Units sold vs the preceding equal-length window.</param>
/// <param name="TopItems">Up to 10 items by Σ Qty, highest first.</param>
/// <param name="SlowItems">Up to 10 of the supplier's available catalog items with the LEAST demand in the window (zero-demand items included), lowest first.</param>
/// <param name="ByBuyer">Per client tenant, highest revenue first.</param>
/// <param name="RevenueTrend">Daily revenue/order-count points across the window, oldest first (chart-ready).</param>
public sealed record SupplierAnalyticsDto(
    DateOnly From,
    DateOnly To,
    decimal TotalRevenue,
    int OrderCount,
    decimal ItemsSold,
    PeriodMetricDto RevenueDelta,
    PeriodMetricDto OrderCountDelta,
    PeriodMetricDto ItemsSoldDelta,
    IReadOnlyList<SupplierAnalyticsItemDto> TopItems,
    IReadOnlyList<SupplierAnalyticsItemDto> SlowItems,
    IReadOnlyList<SupplierAnalyticsBuyerDto> ByBuyer,
    IReadOnlyList<SupplierAnalyticsTrendPointDto> RevenueTrend);

/// <summary>One item row in <see cref="SupplierAnalyticsDto.TopItems"/> / <see cref="SupplierAnalyticsDto.SlowItems"/>.</summary>
/// <param name="SupplierItemId">Null when the order line's supplier catalog entry was deleted (FK SET NULL).</param>
/// <param name="ItemName">For top items: the order-line name snapshot. For slow items: the current catalog name.</param>
/// <param name="QtySold">Σ Qty of this item across the window (0 for a zero-demand slow item).</param>
/// <param name="Revenue">Σ LineTotal of this item across the window.</param>
/// <param name="OrderCount">Distinct orders that included this item.</param>
public sealed record SupplierAnalyticsItemDto(
    Guid? SupplierItemId,
    string ItemName,
    decimal QtySold,
    decimal Revenue,
    int OrderCount);

/// <summary>Per-buyer breakdown row.</summary>
public sealed record SupplierAnalyticsBuyerDto(
    Guid ClientTenantId,
    string ClientName,
    int OrderCount,
    decimal Revenue);

/// <summary>One day on the revenue trend line.</summary>
public sealed record SupplierAnalyticsTrendPointDto(
    DateOnly Date,
    decimal Revenue,
    int OrderCount);
