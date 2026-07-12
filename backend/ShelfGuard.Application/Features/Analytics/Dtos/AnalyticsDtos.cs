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
