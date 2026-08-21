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
/// Day/week revenue+quantity trend for one product. <see cref="MarginAmount"/> on each point is
/// null unless the caller cleared AnalyticsAuthorization.CanViewMargin (ADR-027) — same "оцінна
/// маржа" retroactive-cost-basis caveat as GetCategoryProductBreakdownAsync (TASK-481): current
/// Item.PricePurchase applied retroactively, not the batch cost at the time of each historical
/// sale. See <see cref="ProductSalesTrendComparisonDto"/> (TASK-590) for the compare-mode variant.
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

// ── TASK-590: single-product sales trend vs. baseline comparison (Events calendar — a product
// linked to a demand event, e.g. Easter → paska bread, sold during the event's date window vs.
// the equal-length period immediately preceding it) ─────────────────────────────────────────

/// <summary>
/// Compare-mode variant of <see cref="ProductSalesTrendDto"/>: current window vs. baseline
/// (equal-length period immediately preceding <see cref="From"/> by default — see
/// AnalyticsController.ResolveCompareRange). <see cref="Comparison"/> points are not zero-filled
/// for gap days, same convention as <see cref="ProductSalesTrendDto"/>/
/// <see cref="PosRevenueTrendComparisonDto"/> — the frontend should align series by day-offset
/// from <see cref="From"/>/<see cref="CompareFrom"/>, not raw array index. <see cref="Comparison"/>
/// is an empty list (never null) when the product had zero sales in the baseline window — a
/// routine case (product only started selling recently, or genuinely had none before the event),
/// not an error. *PercentChange is null when the corresponding baseline total is zero (see
/// AnalyticsService.PercentChange's "previous == 0 → null" convention).
/// </summary>
public sealed record ProductSalesTrendComparisonDto(
    Guid ProductId,
    string ProductName,
    IReadOnlyList<ProductSalesTrendPointDto> Current,
    IReadOnlyList<ProductSalesTrendPointDto> Comparison,
    string GroupBy,
    DateOnly From, DateOnly To,
    DateOnly CompareFrom, DateOnly CompareTo,
    decimal CurrentTotalRevenue, decimal ComparisonTotalRevenue, decimal? RevenuePercentChange,
    decimal CurrentTotalQuantity, decimal ComparisonTotalQuantity, decimal? QuantityPercentChange);

// ── TASK-490: worst-performing products / dead stock (pos/worst-products) ───────────────────

/// <summary>
/// The dead-stock counterpart to <see cref="PosTopProductsDto"/> -- but not simply that query
/// sorted ascending. That query groups PosTransactionItems, so a product with zero sales in the
/// period never appears at all (no rows to group). This DTO's source query instead starts from
/// the catalog/stock side (active items currently on-hand) and LEFT-JOINs the sales rollup, so a
/// zero-sale product still shows up with <see cref="WorstProductRowDto.SalesRevenue"/> == 0.
/// <see cref="WorstProductRowDto.CurrentStock"/> is what makes a zero-revenue row actionable --
/// it's the evidence of exactly how many units are sitting unsold. Products are ordered ascending
/// by SalesRevenue (worst/zero first) and capped at the caller's `limit`.
/// </summary>
public sealed record WorstProductsDto(
    IReadOnlyList<WorstProductRowDto> Products);

public sealed record WorstProductRowDto(
    Guid ProductId,
    string ProductName,
    decimal SalesRevenue,
    decimal UnitsSold,
    int TransactionCount,
    decimal CurrentStock);
