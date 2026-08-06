using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;

// ── GET segments (list) ──────────────────────────────────────────────────────────────────────

public sealed record PostCampaignSegmentListItemDto(
    Guid Id,
    string? Name,
    DateTimeOffset CreatedAt,
    int UploadedCount,
    int MatchedCount,
    bool IsAnalyzed,
    DateOnly? AfterStart,
    DateOnly? AfterEnd,
    DateTimeOffset? AnalyzedAt);

// ── POST segments/import ─────────────────────────────────────────────────────────────────────

/// <summary>Application-layer input for the import step — deliberately free of ASP.NET types
/// (no <c>IFormFile</c>); the controller reads the uploaded file into <see cref="FileBytes"/>
/// before calling the service, keeping HTTP concerns out of the Application layer (mirrors how
/// <c>DailySalesController.ImportCsv</c> already reads its <c>IFormFile</c> into a plain string
/// before calling <c>DailySalesService.ImportCsvAsync</c>). Exactly one of <see cref="RawText"/>/
/// <see cref="FileBytes"/> must be set — the controller validates this before calling in.</summary>
public sealed record PostCampaignImportRequest(
    string? RawText,
    byte[]? FileBytes,
    string? FileName,
    string? Name,
    int? ColumnIndex);

/// <summary>Echoes back which column was used and a preview of every column, for a frontend
/// column-picker (brief §6.3) — null for raw-text imports, which have no tabular structure.</summary>
public sealed record PostCampaignImportColumnPreviewDto(
    IReadOnlyList<string> Headers,
    int DetectedColumnIndex,
    string? DetectedColumnHeader,
    IReadOnlyList<IReadOnlyList<string?>> SampleRows);

public sealed record PostCampaignImportResultDto(
    Guid SegmentId,
    string? Name,
    int UploadedCount,
    int MatchedCount,
    int DuplicateCount,
    int UnknownCount,
    int InvalidCount,
    IReadOnlyList<string> UnknownTokensSample,
    IReadOnlyList<string> InvalidTokensSample,
    PostCampaignImportColumnPreviewDto? ColumnPreview,
    DateTimeOffset CreatedAt);

// ── POST segments/{id}/analyze ───────────────────────────────────────────────────────────────

public sealed record PostCampaignAnalyzeRequest(DateOnly AfterStart, DateOnly AfterEnd);

public sealed record PostCampaignAnalyzeResultDto(
    Guid SegmentId,
    DateOnly AfterStart,
    DateOnly AfterEnd,
    DateOnly BeforeStart,
    DateOnly BeforeEnd,
    string SegmentHash,
    DateTimeOffset AnalyzedAt);

// ── Recommendations (embedded in Summary/Migration responses) ───────────────────────────────

public sealed record PostCampaignRecommendationDto(string TriggerUa, string ActionUa, string OfferUa, string CautionUa);

/// <summary>Narrowed inputs <see cref="PostCampaignRecommendationTemplates.BuildSummary"/> needs
/// — same "input DTO separate from the wire DTO" shape as Фаза 1's <c>RfmRecommendationInputDto</c>.</summary>
public sealed record PostCampaignSummaryRecommendationInputDto(
    int MatchedCount,
    decimal MoneyAfter,
    decimal? MoneyDeltaPercent,
    int Reactivated,
    decimal? ReactivationRatePercent,
    int Dropped,
    int BuyersAfter);

public sealed record PostCampaignMigrationRecommendationInputDto(int UpCount, int StableCount, int DownCount);

// ── GET segments/{id}/summary ────────────────────────────────────────────────────────────────

public sealed record PostCampaignSummaryDto(
    int MatchedCount,
    decimal MoneyBefore,
    decimal MoneyAfter,
    decimal? MoneyDeltaPercent,
    int BuyersAfter,
    int Reactivated,
    decimal? ReactivationRatePercent,
    int InactiveBefore,
    int Retained,
    decimal? RetentionRatePercent,
    int ActiveBefore,
    int Dropped,
    decimal? ChurnRatePercent,
    int NotReturned,
    PostCampaignRecommendationDto Recommendation,
    string SegmentHash,
    DateOnly AfterStart,
    DateOnly AfterEnd,
    DateOnly BeforeStart,
    DateOnly BeforeEnd,
    DateTimeOffset CalculatedAt);

// ── GET segments/{id}/daily-turnover ─────────────────────────────────────────────────────────

/// <summary><see cref="DayIndex"/> is 1-based ordinal day-in-window, shared between the before
/// and after series (source doc §15: aligned by ordinal day, not calendar date).
/// <see cref="AfterCalendarDate"/> is the real after-window date for that ordinal day, used for
/// the chart's X-axis labels (source doc: "Вісь X підписана календарними датами after-вікна").</summary>
public sealed record PostCampaignDailyTurnoverPointDto(
    int DayIndex, DateOnly AfterCalendarDate, decimal BeforeAmount, decimal AfterAmount);

public sealed record PostCampaignDailyTurnoverDto(
    IReadOnlyList<PostCampaignDailyTurnoverPointDto> Points,
    decimal TotalBefore,
    decimal TotalAfter,
    string SegmentHash,
    DateOnly AfterStart,
    DateOnly AfterEnd,
    DateOnly BeforeStart,
    DateOnly BeforeEnd,
    DateTimeOffset CalculatedAt);

// ── GET segments/{id}/rfm-activity ───────────────────────────────────────────────────────────

/// <summary>Every *Percent field is null (never 0%) when its before-value denominator is zero —
/// source doc §36.7. <see cref="RecencyDenominatorBefore"/>/<see cref="RecencyDenominatorAfter"/>
/// expose exactly how many segment members had a purchase in that window at all, since customers
/// with none are excluded from the recency average rather than pulled in with a made-up value
/// (source doc §22.1's "show the denominator" transparency requirement).</summary>
public sealed record PostCampaignRfmActivityDto(
    int ChecksBefore,
    int ChecksAfter,
    decimal? ChecksDeltaPercent,
    decimal TurnoverBefore,
    decimal TurnoverAfter,
    decimal? TurnoverDeltaPercent,
    decimal? AverageCheckBefore,
    decimal? AverageCheckAfter,
    decimal? AverageCheckDeltaPercent,
    decimal? RecencyBeforeDays,
    decimal? RecencyAfterDays,
    decimal? RecencyDeltaPercent,
    int RecencyDenominatorBefore,
    int RecencyDenominatorAfter,
    string SegmentHash,
    DateOnly AfterStart,
    DateOnly AfterEnd,
    DateOnly BeforeStart,
    DateOnly BeforeEnd,
    DateTimeOffset CalculatedAt);

// ── GET segments/{id}/customers ──────────────────────────────────────────────────────────────

/// <summary><see cref="SegmentBefore"/>/<see cref="SegmentAfter"/> are null for Фаза 1's dedicated
/// "no purchase" bucket (a customer with zero all-time purchases as of that window's end) — see
/// <c>PostCampaignService.ClassifyForWindow</c>. Label fields are pre-resolved server-side so the
/// frontend never needs its own copy of <c>RfmSegmentCatalog</c>'s label mapping.</summary>
public sealed record PostCampaignCustomerRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    int ChecksBefore,
    int ChecksAfter,
    decimal TurnoverBefore,
    decimal TurnoverAfter,
    RfmSegmentKey? SegmentBefore,
    string SegmentBeforeLabelUa,
    RfmSegmentKey? SegmentAfter,
    string SegmentAfterLabelUa);

/// <summary>Full server pagination, NOT a Top-200 cap — the explicit fix over the competitor
/// (source doc §23.3/§35.3).</summary>
public sealed record PostCampaignCustomerTableDto(
    int TotalCount,
    IReadOnlyList<PostCampaignCustomerRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    string SegmentHash,
    DateTimeOffset CalculatedAt);

// ── GET segments/{id}/migration ──────────────────────────────────────────────────────────────

/// <summary><see cref="Segment"/> null = Фаза 1's "Без покупок" special case (zero all-time
/// purchases as of that window's end), rendered with <c>RfmSegmentCatalog.NoPurchaseLabelUa</c>.</summary>
public sealed record PostCampaignSegmentDistributionItemDto(
    RfmSegmentKey? Segment, string LabelUa, int Count, decimal SharePercent);

public sealed record PostCampaignMigrationCellDto(
    RfmSegmentKey? Before, string BeforeLabelUa, RfmSegmentKey? After, string AfterLabelUa, int Count);

public sealed record PostCampaignMigrationDto(
    IReadOnlyList<PostCampaignSegmentDistributionItemDto> BeforeDistribution,
    IReadOnlyList<PostCampaignSegmentDistributionItemDto> AfterDistribution,
    IReadOnlyList<PostCampaignMigrationCellDto> Matrix,
    int UpCount,
    int StableCount,
    int DownCount,
    PostCampaignRecommendationDto Recommendation,
    string SegmentHash,
    DateOnly AfterStart,
    DateOnly AfterEnd,
    DateOnly BeforeStart,
    DateOnly BeforeEnd,
    DateTimeOffset CalculatedAt);

// ── POST segments/{id}/explain ───────────────────────────────────────────────────────────────

public sealed record ExplainPostCampaignResultDto(string ExplanationUa, string Model, int TokensUsed);

// ── Exports ───────────────────────────────────────────────────────────────────────────────────

/// <summary>Own-filter fields only — same flat, standalone export-request shape as Фаза 1/2/3's
/// own export requests (e.g. <c>ExportAudienceBuyersRequest</c>).</summary>
public sealed record ExportPostCampaignCustomersRequest(IReadOnlyList<Guid>? StoreIds, bool UnmaskPii);

public sealed record PostCampaignExportResult(byte[] FileBytes, string FileName, int RowCount, bool Truncated);

/// <summary>
/// TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20) — dedicated result
/// shape for <c>ExportUnknownTokensAsync</c>, distinct from the generic
/// <see cref="PostCampaignExportResult"/> shared by every other export in this feature.
/// <see cref="PostCampaignExportResult.Truncated"/> only ever reflects
/// <see cref="Common.IExcelExportService"/>'s own generic 50,000-row ceiling — meaningless here,
/// since this export is built from <see cref="Domain.Entities.PostCampaignSegment.UnknownTokensSample"/>/
/// <see cref="Domain.Entities.PostCampaignSegment.InvalidTokensSample"/>, which are capped far
/// lower, at IMPORT time. <see cref="TotalUnknownCount"/>/<see cref="TotalInvalidCount"/> are the
/// segment's real, uncapped <c>UnknownCount</c>/<c>InvalidCount</c> fields — never capped, only
/// the sample lists are — so a caller can always tell how many tokens exist even when the file
/// itself only contains a sample. <see cref="Truncated"/> is true whenever the exported sample is
/// a strict subset of the real count for either kind (the exported file also carries a visible
/// in-sheet note in that case — see <c>PostCampaignService.ExportUnknownTokensAsync</c>).
/// </summary>
public sealed record PostCampaignUnknownTokensExportResult(
    byte[] FileBytes, string FileName, int RowCount, int TotalUnknownCount, int TotalInvalidCount, bool Truncated);
