using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;

// ── "Частота та реактивація" tab (TASK-420 design doc §2 / §4, analysis doc §16-21). No
// "all-time" mode here (design doc: "БЕЗ 'весь час' — для аналізу зміни частоти потрібні два
// порівнювані часові вікна", analysis doc §16 confirms the competitor's own UI omits it too). ──

// ── GET price-segments/frequency/overview ───────────────────────────────────────────────────

/// <summary>One of the 4 "Частота" cards. <see cref="SharePercent"/> is of the UNION population
/// (current ∪ previous buyers) — frequency's natural denominator (analysis doc §16.3), NOT
/// "Проаналізовано" (that's the comparison-cohort/INTERSECTION concept the price tab uses —
/// deliberately a different population, see <see cref="FrequencyAudienceKey"/>'s own doc).</summary>
public sealed record FrequencyAudienceSummaryDto(
    FrequencyAudienceKey Audience, string LabelUa, int CustomerCount, decimal SharePercent, decimal AverageLtv);

/// <summary>
/// <see cref="AtRiskPercentOfUnionPopulation"/> and <see cref="AtRiskPercentOfActiveCurrentBuyers"/>
/// are the SAME numerator (Sleeping ∪ Declining) over two different denominators — analysis doc
/// §17.6/§25.2 documents the competitor showing only the (arguably misleading, since Sleeping
/// customers have zero CURRENT purchases) "% of active current buyers" figure with no
/// alternative; this DTO keeps both explicit so the frontend never has to guess or re-derive one
/// from the other. <see cref="AverageFrequencyCurrent"/>/<see cref="AverageFrequencyPrevious"/>
/// and <see cref="AverageSpendCurrentPeriod"/> are computed over the ACTIVE-CURRENT population
/// (customers with at least one purchase in the current window) — analysis doc §16.1's own KPI
/// row pairs "Активних покупців" with "Середня частота"/"Було" under the same 30 936 base, a
/// product decision this codebase makes explicit here since the competitor's UI never states its
/// denominator directly (design doc §30).
/// </summary>
public sealed record FrequencyOverviewDto(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly PreviousPeriodFrom,
    DateOnly PreviousPeriodTo,
    int ActiveCurrentBuyerCount,
    decimal ActiveBuyerCountChangePercent,
    decimal AverageFrequencyCurrent,
    decimal AverageFrequencyPrevious,
    int UnionPopulationCount,
    int AtRiskCount,
    decimal AtRiskPercentOfUnionPopulation,
    decimal AtRiskPercentOfActiveCurrentBuyers,
    decimal AverageSpendCurrentPeriod,
    IReadOnlyList<FrequencyAudienceSummaryDto> Audiences,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── GET price-segments/frequency/audiences/{audience} ──────────────────────────────────────

/// <summary>
/// <see cref="MinSpend"/>/<see cref="MaxSpend"/>/<see cref="PriceSegmentFilter"/> re-orient to the
/// PREVIOUS period's figures when <paramref name="Audience"/> is <see cref="FrequencyAudienceKey.Sleeping"/>
/// (design doc §4 — a sleeping customer's CURRENT spend/typical-check are always zero/null by
/// definition, so filtering by current-period figures would silently return nothing, the exact
/// competitor bug analysis doc §20.2/§25.4 documents). <see cref="DeclineThresholdPercent"/> is
/// only meaningful for <see cref="FrequencyAudienceKey.Declining"/> but is always applied here
/// (not just for that one audience) because it also determines which customers fall into
/// "Declining" vs "Other" for every other audience's classification pass over the SAME union
/// population — the repository classifies ALL audiences from one shared CASE ladder. Null means
/// "use the tenant's persisted <see cref="Domain.Entities.PriceSegmentSettings.
/// DefaultFrequencyDeclineThresholdPercent"/>" — resolved once by the service, same lazy-default
/// rule <see cref="IPriceSegmentsService.GetFrequencyOverviewAsync"/> already follows.
/// <see cref="CanViewUnmaskedPii"/>: see <see cref="PriceAudienceTableRequest.CanViewUnmaskedPii"/>
/// (TASK-425) — same server-derived, non-client-bindable capability flag, resolved by the
/// controller via <c>MarketingAnalyticsAuthorization.CanExportPii(User)</c>.</summary>
public sealed record FrequencyAudienceTableRequest(
    FrequencyAudienceKey Audience,
    DateOnly From, DateOnly To, IReadOnlyList<Guid>? StoreIds,
    decimal? DeclineThresholdPercent,
    decimal? MinSpend, decimal? MaxSpend, PriceSegmentKey? PriceSegmentFilter,
    int Page, int PageSize, string? SortBy, bool SortDescending, bool CanViewUnmaskedPii = false);

/// <summary>
/// <see cref="TypicalCheckCurrent"/> is null whenever the customer has zero CURRENT-period
/// receipts (always true for every <see cref="FrequencyAudienceKey.Sleeping"/> row) — the
/// frontend renders "—", never 0 ₴ (design doc §4 / analysis doc §21: "типовий чек — '—'").
/// <see cref="FrequencyDeltaPercent"/> is null whenever <see cref="PreviousFrequency"/> is 0
/// (division by zero avoided, never shown as "∞" — analysis doc §17.3/§29.6: "Відсоткова зміна
/// для нульової бази показується як '—'"). <see cref="SpendCurrentPeriod"/> is always the
/// CURRENT period's spend regardless of audience/filter re-orientation (0 for Sleeping, by
/// definition — analysis doc §21: "витрати — 0" for a sleeping row) — only the FILTER
/// (<see cref="FrequencyAudienceTableRequest.MinSpend"/>/MaxSpend) re-orients to previous, never
/// the displayed column.</summary>
public sealed record FrequencyAudienceRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    int PreviousFrequency,
    int CurrentFrequency,
    int FrequencyDeltaAbsolute,
    decimal? FrequencyDeltaPercent,
    decimal? TypicalCheckCurrent,
    decimal SpendCurrentPeriod,
    decimal Ltv);

public sealed record FrequencyAudienceTableDto(
    FrequencyAudienceKey Audience,
    string LabelUa,
    int TotalCount,
    int WithPhoneCount,
    IReadOnlyList<FrequencyAudienceRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    PriceSegmentRecommendationDto Recommendation,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Export ───────────────────────────────────────────────────────────────────────────────────

public sealed record ExportFrequencyAudienceRequest(
    FrequencyAudienceKey Audience, DateOnly From, DateOnly To, IReadOnlyList<Guid>? StoreIds,
    decimal? DeclineThresholdPercent, decimal? MinSpend, decimal? MaxSpend, PriceSegmentKey? PriceSegmentFilter,
    bool UnmaskPii);

// ── Recommendation input — Application-internal, feeds PriceSegmentRecommendationTemplates,
// never serialized to the wire directly (same separation the comparison/all-time inputs keep
// in PriceSegmentDtos.cs). ────────────────────────────────────────────────────────────────────

public sealed record FrequencyAudienceRecommendationInputDto(
    FrequencyAudienceKey Audience, int CustomerCount, decimal SharePercent, decimal AverageLtv,
    decimal AverageFrequencyPrevious, decimal AverageFrequencyCurrent);
