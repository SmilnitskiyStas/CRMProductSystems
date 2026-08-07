namespace ShelfGuard.Application.Features.Analytics.Dtos;

public sealed record PosAnalyticsSummaryDto(
    decimal TotalRevenue,
    int TransactionCount,
    decimal AverageTicket,
    decimal CashRevenue,
    decimal CardRevenue,
    int ShiftCount,
    DateOnly From,
    DateOnly To);

public sealed record PosRevenueTrendDto(
    IReadOnlyList<RevenueTrendPointDto> Points,
    string GroupBy);

public sealed record RevenueTrendPointDto(
    DateOnly Date,
    decimal Revenue,
    int Transactions);

// ── TASK-336: period comparison ─────────────────────────────────────────────

public sealed record PosSummaryComparisonDto(
    PosAnalyticsSummaryDto Current,
    PosAnalyticsSummaryDto? Comparison,
    decimal? RevenuePercentChange,
    decimal? TransactionCountPercentChange);

/// <summary>
/// Current/Comparison points are each sorted ascending by their own Date and not
/// zero-filled for gap days (matches PosRevenueTrendDto behavior). The frontend
/// should align series by day-offset from From/CompareFrom respectively, not by
/// raw array index, since either series can have missing days.
/// </summary>
public sealed record PosRevenueTrendComparisonDto(
    IReadOnlyList<RevenueTrendPointDto> Current,
    IReadOnlyList<RevenueTrendPointDto>? Comparison,
    string GroupBy,
    DateOnly From,
    DateOnly To,
    DateOnly? CompareFrom,
    DateOnly? CompareTo);

public sealed record PosTopProductsDto(
    IReadOnlyList<TopProductDto> Items);

public sealed record TopProductDto(
    Guid ProductId,
    string ProductName,
    string Barcode,
    decimal TotalRevenue,
    decimal TotalQuantity,
    int TransactionCount);

public sealed record PosCashierStatsDto(
    IReadOnlyList<CashierStatDto> Cashiers);

public sealed record CashierStatDto(
    Guid CashierId,
    string CashierName,
    decimal TotalRevenue,
    int TransactionCount,
    decimal AverageTicket,
    int ShiftCount);

// ── TASK-482: single-product sales trend (row-click drill-down from pos/top-products) ───────

/// <summary>
/// Day/week revenue+quantity trend for one product. No compare-mode variant — this is a
/// snapshot drill-down off a table row, not a page-level KPI trend (see PosRevenueTrendDto for
/// that). <see cref="MarginAmount"/> on each point is null unless the caller cleared
/// AnalyticsAuthorization.CanViewMargin (ADR-027) — same "оцінна маржа" retroactive-cost-basis
/// caveat as GetCategoryProductBreakdownAsync (TASK-481): current Item.PricePurchase applied
/// retroactively, not the batch cost at the time of each historical sale.
/// </summary>
public sealed record ProductSalesTrendDto(
    Guid ProductId,
    string ProductName,
    IReadOnlyList<ProductSalesTrendPointDto> Points,
    string GroupBy);

public sealed record ProductSalesTrendPointDto(
    DateOnly Date,
    decimal Revenue,
    decimal Quantity,
    int TransactionCount,
    decimal? MarginAmount);
