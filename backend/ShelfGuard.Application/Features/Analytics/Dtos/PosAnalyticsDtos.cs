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
