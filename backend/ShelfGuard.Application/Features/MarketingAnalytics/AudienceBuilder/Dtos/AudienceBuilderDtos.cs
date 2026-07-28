using System.Text.Json.Serialization;

namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;

// ── Term kind / combine mode / competitor horizon — request-shape toggles the caller picks, not
// computed classification outcomes (unlike RfmSegmentKey/PriceAudienceKey/FrequencyAudienceKey,
// which live in Domain.Constants because they're the OUTPUT of a pure classifier). Design doc §7
// places these directly alongside the request DTOs, so they live here rather than Domain.Constants. ─

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudienceTermKind { Text, Category }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudienceCombineMode { Any, All }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompetitorHorizon { InPeriod, AllTime }

/// <summary>
/// One chip in the term-builder (design doc §2/§7). <see cref="Text"/> is read only when <see
/// cref="Kind"/> is <see cref="AudienceTermKind.Text"/> (free text — matched against
/// Item.Id/Barcodes/Name, see IAudienceBuilderRepository's remarks); <see cref="CategoryId"/> is
/// read only when <see cref="Kind"/> is <see cref="AudienceTermKind.Category"/>. A term missing
/// its own Kind's required value is silently dropped when the service resolves the query — never
/// throws, never counts toward the AND-mode term total (same defensive posture as an empty Terms
/// list resolving to "no query at all, zeroed result").
/// </summary>
public sealed record AudienceTermRequest(AudienceTermKind Kind, string? Text, Guid? CategoryId);

// ── GET .../categories ──────────────────────────────────────────────────────────────────────

public sealed record AudienceCategoryOptionDto(Guid CategoryId, string Name, int ItemCount);

// ── Own-product audience: POST overview / buyers / matched-items ───────────────────────────────

/// <summary>
/// Shared filter shape for every "own product" read endpoint (design doc §7 — one flat request
/// type reused across overview/buyers/matched-items, exactly like the design doc's own
/// <c>AudienceBuildRequest</c> sketch). <see cref="Page"/>/<see cref="PageSize"/>/<see
/// cref="SortBy"/>/<see cref="SortDescending"/> are ignored by the overview endpoint (a single
/// aggregate row, never paginated). <see cref="CanViewUnmaskedPii"/> (TASK-425 pattern, applied
/// from day 0 per design doc §9) is resolved server-side by the controller via
/// <c>MarketingAnalyticsAuthorization.CanExportPii(User)</c> — never bound from client input on
/// the read endpoints; defaults to <c>false</c> (masked) so any call site that forgets to set it
/// explicitly fails closed.
/// </summary>
public sealed record AudienceBuildRequest(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    IReadOnlyList<AudienceTermRequest> Terms,
    AudienceCombineMode Mode,
    decimal? MinQuantity,
    decimal? MinAmount,
    IReadOnlyList<Guid>? ExcludedItemIds,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = true,
    bool CanViewUnmaskedPii = false);

/// <summary>
/// Own-filter fields only, no paging — same flat, standalone shape as Фаза 2's
/// <c>ExportPriceAudienceRequest</c>: export always fetches the FULL (thresholded, capped-at-50k)
/// result server-side, never the caller's current on-screen page/sort.
/// </summary>
public sealed record ExportAudienceBuyersRequest(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    IReadOnlyList<AudienceTermRequest> Terms,
    AudienceCombineMode Mode,
    decimal? MinQuantity,
    decimal? MinAmount,
    IReadOnlyList<Guid>? ExcludedItemIds,
    bool UnmaskPii);

public sealed record AudienceOverviewDto(
    int ParticipantsCount,
    int ItemsInSelectionCount,
    decimal UnitsPurchased,
    decimal TotalSpend,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

public sealed record AudienceBuyerRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    decimal QuantityPurchased,
    int ReceiptCount,
    decimal TotalAmount);

public sealed record AudienceBuyerTableDto(
    int TotalCount,
    int WithPhoneCount,
    IReadOnlyList<AudienceBuyerRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Design doc §6/§20 — every SKU that matched the search, INCLUDING ones the caller has already
/// excluded (<see cref="IsExcluded"/> = true, checkbox unchecked in the "Знайдені товари" tab) and
/// ones with zero sales in the active period (<see cref="QuantitySold"/>/<see
/// cref="ReceiptCount"/>/<see cref="BuyerCount"/> all 0 — a deliberately included row, not a gap:
/// analysis doc §20.4 confirms the competitor shows these too).
/// </summary>
public sealed record MatchedItemRowDto(
    Guid ItemId,
    string Name,
    string? BarcodesJoined,
    bool IsExcluded,
    decimal QuantitySold,
    int ReceiptCount,
    int BuyerCount);

public sealed record MatchedItemsTableDto(
    int TotalCount,
    IReadOnlyList<MatchedItemRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Competitor audience: POST competitor/overview / competitor/buyers ──────────────────────────

/// <summary>
/// <see cref="OwnTerms"/>/<see cref="OwnExcludedItemIds"/> are the SAME term-builder state already
/// used for the main "Покупці товару" tab (design doc §5/§10: one shared Zustand store — the
/// competitor tab only adds its own competitor-term chips on top, it does not re-collect the own-
/// product terms). There is deliberately no <c>OwnMode</c>/min-quantity/min-amount here: design doc
/// §5's <c>own_buyers_to_exclude</c> is a plain "bought ANY unit of ANY own-matching item, ever/in-
/// period" existence check, never gated by AND/OR mode or the quantity/amount thresholds (those
/// only filter the MAIN audience's customer list, never the competitor tab's exclusion set).
/// </summary>
public sealed record CompetitorAudienceRequest(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    IReadOnlyList<AudienceTermRequest> OwnTerms,
    IReadOnlyList<Guid>? OwnExcludedItemIds,
    IReadOnlyList<AudienceTermRequest> CompetitorTerms,
    CompetitorHorizon Horizon,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = true,
    bool CanViewUnmaskedPii = false);

public sealed record ExportCompetitorBuyersRequest(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    IReadOnlyList<AudienceTermRequest> OwnTerms,
    IReadOnlyList<Guid>? OwnExcludedItemIds,
    IReadOnlyList<AudienceTermRequest> CompetitorTerms,
    CompetitorHorizon Horizon,
    bool UnmaskPii);

public sealed record CompetitorOverviewDto(
    int NewAudienceCount,
    int CompetitorItemsCount,
    decimal UnitsPurchased,
    decimal TotalSpend,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

public sealed record CompetitorBuyerRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    decimal QuantityPurchased,
    int ReceiptCount,
    decimal TotalAmount);

public sealed record CompetitorBuyerTableDto(
    int TotalCount,
    int WithPhoneCount,
    IReadOnlyList<CompetitorBuyerRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Exports ──────────────────────────────────────────────────────────────────────────────────

/// <summary>Result of a completed export — bytes are the finished .xlsx file, streamed straight
/// into the HTTP response by the controller, same shape as Фаза 1/2's own export result records.</summary>
public sealed record AudienceBuilderExportResult(byte[] FileBytes, string FileName, int RowCount, bool Truncated);
