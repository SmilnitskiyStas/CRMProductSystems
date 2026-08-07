namespace ShelfGuard.Application.Features.Analytics.Dtos;

// ── TASK-336: period comparison (dashboard week-over-week + analytics compare) ─

public sealed record PeriodMetricDto(decimal Current, decimal Previous, decimal? PercentChange)
{
    public static PeriodMetricDto Of(decimal current, decimal previous) =>
        new(current, previous, previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 2));
}

public sealed record WeeklyKpiDto(
    PeriodMetricDto Sales,
    PeriodMetricDto Revenue,
    PeriodMetricDto WriteOffLoss
);

public sealed record ExpirySummaryComparisonDto(ExpirySummaryDto Current, ExpirySummaryDto? Previous);

public sealed record WriteOffsComparisonDto(
    WriteOffAnalyticsDto Current,
    WriteOffAnalyticsDto? Comparison,
    decimal? TotalLossPercentChange
);

public sealed record LossesComparisonDto(
    LossesDto Current,
    LossesDto? Comparison,
    decimal? TotalLossPercentChange
);

public sealed record ExpirySummaryDto(
    int Safe,
    int Warning,
    int Critical,
    int Expired,
    int NeedsVerification,
    int Total,
    IReadOnlyList<ExpirySummaryStoreDto> Stores
);

public sealed record ExpirySummaryStoreDto(
    Guid StoreId,
    string StoreName,
    int Safe,
    int Warning,
    int Critical,
    int Expired
);

public sealed record WriteOffAnalyticsDto(
    int TotalDocuments,
    decimal TotalLoss,
    IReadOnlyList<WriteOffByReasonDto> ByReason,
    IReadOnlyList<WriteOffByDateDto> ByDate
);

public sealed record WriteOffByReasonDto(string Reason, int Count, decimal TotalLoss);

public sealed record WriteOffByDateDto(DateOnly Date, int Count, decimal TotalLoss);

public sealed record MovementAnalyticsDto(
    int TotalMovements,
    decimal TotalQuantity,
    IReadOnlyList<MovementByTypeDto> ByType
);

public sealed record MovementByTypeDto(string MovementType, int Count, decimal TotalQuantity);

public sealed record ZoneAnalyticsDto(
    Guid ZoneId,
    string ZoneName,
    string ZoneType,
    Guid StoreId,
    string StoreName,
    int Safe,
    int Warning,
    int Critical,
    int Expired,
    int TotalBatches
);

public sealed record CategoryAnalyticsDto(
    Guid? CategoryId,
    string CategoryName,
    int Safe,
    int Warning,
    int Critical,
    int Expired,
    int TotalBatches,
    decimal TotalQuantity
);

public sealed record LossesDto(
    decimal TotalLoss,
    int TotalWriteOffs,
    decimal AverageLossPerWriteOff,
    IReadOnlyList<LossByStoreDto> ByStore
);

public sealed record LossByStoreDto(
    Guid StoreId,
    string StoreName,
    decimal TotalLoss,
    int WriteOffCount
);

// ── TASK-481 (interactive analytics + margin plan): category/losses product drill-down ────

public sealed record CategoryProductBreakdownDto(
    Guid? CategoryId,
    string CategoryName,
    IReadOnlyList<CategoryProductRowDto> Products
);

// MarginAmount/MarginPercent are null both when the caller can't see margin (ADR-027 —
// AnalyticsAuthorization.CanViewMargin resolved false) and when the product itself has no
// Item.PricePurchase on file — two different "no value" cases the controller/repository must
// not conflate into a shared 0 or omitted field.
//
// DaysOfStockRemaining (TASK-491): TotalQuantity / ProductAdu.AduEffective at the current sell
// rate. Never margin/cost data (no AnalyticsAuthorization.CanViewMargin gate). Null — never 0,
// never a huge/infinite number — in two independent cases: (a) the request wasn't store-scoped
// (ProductAdu is per-(product, store), so a network-wide/multi-store rollup has no single
// meaningful ADU to divide by); (b) the product has no ProductAdu row, or its AduEffective is
// null/0 (division-by-zero guard — "no usage history yet" is a real, valid state, not an error).
public sealed record CategoryProductRowDto(
    Guid ProductId,
    string ProductName,
    int Safe,
    int Warning,
    int Critical,
    int Expired,
    decimal TotalQuantity,
    decimal SalesRevenue,
    decimal UnitsSold,
    decimal? MarginAmount,
    decimal? MarginPercent,
    decimal? DaysOfStockRemaining
);

public sealed record LossesByProductDto(
    decimal TotalLoss,
    IReadOnlyList<LossByProductRowDto> Products
);

public sealed record LossByProductRowDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal LossAmount,
    decimal SharePercent
);

// ── TASK-489: losses/write-offs trend over time (mirrors PosRevenueTrendDto's shape) ───────

public sealed record LossesTrendDto(IReadOnlyList<LossesTrendPointDto> Points, string GroupBy);

public sealed record LossesTrendPointDto(DateOnly Date, decimal TotalLoss, int Count);
