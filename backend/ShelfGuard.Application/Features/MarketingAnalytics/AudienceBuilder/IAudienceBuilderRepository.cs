namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;

// ── Resolved queries — built by the SERVICE from the wire-facing request DTOs (splitting a
// Terms list into parallel text/category index+value arrays for the UNNEST-based SQL below),
// handed to the repository as one shape instead of 10+ primitive parameters (design doc §7:
// "Репозиторій приймає internal-record ResolvedAudienceQuery/ResolvedCompetitorQuery замість
// довгого списку примітивів"). Dates are already UTC DateTime (not DateOnly) and store/excluded-
// item id lists are already deduplicated arrays — the repository never re-derives either. ────────

/// <summary><see cref="TermCount"/> counts only WELL-FORMED terms (a Text term with empty text, or
/// a Category term with no CategoryId, is dropped before this record is built and never counts
/// toward it) — see <c>AudienceBuilderService.SplitTerms</c>. <see cref="MatchAll"/> = true means
/// AND (design doc §3: a customer must cover every term_index at least once, not own every
/// matched SKU).</summary>
public sealed record ResolvedAudienceQuery(
    Guid TenantId,
    DateTime FromDt,
    DateTime ToDt,
    Guid[] StoreIds,
    int[] TextTermIndexes,
    string[] TextTermValues,
    int[] CategoryTermIndexes,
    Guid[] CategoryTermIds,
    int TermCount,
    bool MatchAll,
    decimal? MinQuantity,
    decimal? MinAmount,
    Guid[] ExcludedItemIds);

/// <summary>Design doc §5 — <see cref="AllTimeHorizon"/> = true switches
/// <c>own_buyers_to_exclude</c> from "in the active period" to "ever, in the full available
/// history" (analysis doc §16/§17's two exclusion modes). <see cref="OwnExcludedItemIds"/> reuses
/// the SAME manual-curation state as the main audience-builder tab (design doc §5/§10 — one shared
/// term-builder form feeds both tabs).</summary>
public sealed record ResolvedCompetitorQuery(
    Guid TenantId,
    DateTime FromDt,
    DateTime ToDt,
    Guid[] StoreIds,
    int[] CompetitorTextTermIndexes,
    string[] CompetitorTextTermValues,
    int[] CompetitorCategoryTermIndexes,
    Guid[] CompetitorCategoryTermIds,
    int[] OwnTextTermIndexes,
    string[] OwnTextTermValues,
    int[] OwnCategoryTermIndexes,
    Guid[] OwnCategoryTermIds,
    Guid[] OwnExcludedItemIds,
    bool AllTimeHorizon);

// ── Raw rows returned by the repository's SQL — the SERVICE maps these into the wire-facing
// Dtos/AudienceBuilderDtos.cs records, same Application/Infrastructure separation every other
// MarketingAnalytics repository already established. ────────────────────────────────────────────

public sealed record AudienceCategoryOptionRow(Guid CategoryId, string Name, int ItemCount);

public sealed record AudienceOverviewRow(int ParticipantsCount, int ItemsInSelectionCount, decimal UnitsPurchased, decimal TotalSpend);

/// <summary><see cref="TotalCount"/>/<see cref="WithPhoneCount"/> are window-function aggregates
/// over the FULL filtered result (repeated identically on every row in the page), same convention
/// as Фаза 2's <c>PriceAudienceTableRowRaw</c>.</summary>
public sealed record AudienceBuyerRowRaw(
    Guid CustomerId, string Name, string? Phone,
    decimal QuantityPurchased, int ReceiptCount, decimal TotalAmount,
    int TotalCount, int WithPhoneCount);

public sealed record MatchedItemRowRaw(
    Guid ItemId, string Name, string? BarcodesJoined, bool IsExcluded,
    decimal QuantitySold, int ReceiptCount, int BuyerCount, int TotalCount);

/// <summary>One receipt line for the receipt-level buyers export (design doc §8) — <see
/// cref="Quantity"/>/<see cref="Amount"/> are the SUM of ONLY the matched/selected SKUs bought on
/// THIS ONE receipt, never the receipt's own grand total (which may include unrelated items).</summary>
public sealed record AudienceReceiptExportRowRaw(
    Guid CustomerId, string Name, string? Phone,
    string ReceiptNumber, DateTime CreatedAt, string? StoreName,
    decimal Quantity, decimal Amount);

public sealed record CompetitorOverviewRow(int NewAudienceCount, int CompetitorItemsCount, decimal UnitsPurchased, decimal TotalSpend);

public sealed record CompetitorBuyerRowRaw(
    Guid CustomerId, string Name, string? Phone,
    decimal QuantityPurchased, int ReceiptCount, decimal TotalAmount,
    int TotalCount, int WithPhoneCount);

/// <summary>
/// Raw-SQL data access for Фаза 3 AudienceBuilder (TASK-429, design doc §1-§8). Third raw-SQL
/// repository in this codebase after <c>IMarketingAnalyticsRepository</c> (Фаза 1) and
/// <c>PriceSegments.IPriceSegmentsRepository</c> (Фаза 2) — term matching by name/barcode/id/
/// category with OR/AND combine, and a competitor-audience MINUS set difference, have no LINQ/
/// EF-translatable equivalent either. Same parameterization discipline as both predecessors: every
/// user-controllable value (tenantId, term text/category-id arrays, store ids, dates, thresholds,
/// excluded-item ids) is a genuine SQL PARAMETER, never string-interpolated; <c>sortBy</c> is
/// translated through <see cref="AudienceBuilderSortKeys"/>' fixed allowlist before it ever reaches
/// the SQL text.
///
/// <b>IMPORTANT — read before touching any <c>i."Name" ILIKE '%...%'</c> predicate below:</b>
/// <c>idx_items_name_trgm</c> (GIN trigram, migration <c>AddItemNameTrigramIndex</c>) is NOT used
/// by the query planner on the real tenant-scoped app connection. <c>items</c> has
/// <c>FORCE ROW LEVEL SECURITY</c>, and <c>ILIKE</c> compiles to the non-LEAKPROOF
/// <c>texticlike</c> function — under FORCE RLS, Postgres can only apply such a predicate as a
/// post-scan <c>Filter</c>, never as an index condition, confirmed live (Seq Scan, ~1085ms over
/// 500k rows on the app role vs ~2ms Bitmap Index Scan on a superuser bypassing RLS against the
/// SAME index and data — see
/// <c>.claude/logs/tasks/428_2026-07-27_item-name-trigram-index_database-engineer.md</c>). This is
/// accepted as a documented v1 limitation at realistic per-tenant catalog sizes (thousands of
/// SKUs — this is a "type a term, press Enter" field, not a live/autocomplete search). Marking
/// <c>texticlike</c> <c>LEAKPROOF</c>, or adding a <c>SECURITY DEFINER</c> search function that
/// re-applies its own hardcoded tenant guard, are possible future fixes — each is a cross-cutting
/// security-posture change needing its own dedicated review, out of scope here.
/// </summary>
public interface IAudienceBuilderRepository
{
    Task<IReadOnlyList<AudienceCategoryOptionRow>> SearchCategoriesAsync(
        Guid tenantId, string? search, int limit, CancellationToken ct = default);

    /// <summary>Single aggregate row — never fetches the per-customer population into memory just
    /// to sum 4 numbers (design doc §7: "окремий легкий агрегатний SQL-запит... НЕ тягнути тисячі
    /// рядків клієнтів у пам'ять").</summary>
    Task<AudienceOverviewRow> GetOverviewAsync(ResolvedAudienceQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<AudienceBuyerRowRaw>> GetBuyersAsync(
        ResolvedAudienceQuery query, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);

    /// <summary>Receipt-level rows for the buyers export (design doc §8) — not paginated, capped
    /// at <paramref name="maxRows"/> via a SQL LIMIT (same "soft cap at the SQL level, the actual
    /// Truncated flag surfaced to the caller is still IExcelExportService's own 50k policy" shape
    /// PriceSegmentsService's own export paths already established).</summary>
    Task<IReadOnlyList<AudienceReceiptExportRowRaw>> GetBuyerReceiptsAsync(
        ResolvedAudienceQuery query, int maxRows, CancellationToken ct = default);

    /// <summary>Every matched SKU (design doc §6/§20) — NOT filtered by ExcludedItemIds (that
    /// would hide already-excluded rows the caller needs to see and can re-include); IsExcluded is
    /// a per-row flag instead. Zero-sales SKUs are included via LEFT JOIN, never dropped.</summary>
    Task<IReadOnlyList<MatchedItemRowRaw>> GetMatchedItemsAsync(
        ResolvedAudienceQuery query, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);

    Task<CompetitorOverviewRow> GetCompetitorOverviewAsync(ResolvedCompetitorQuery query, CancellationToken ct = default);

    /// <summary>Also used for the competitor-buyers export (page=1, pageSize=ExportMaxRows) —
    /// same "reuse the paginated table method for the export" shape as
    /// <c>PriceSegmentsService.ExportAllTimeAsync</c> reusing <c>GetAllTimeCustomerTableAsync</c>.</summary>
    Task<IReadOnlyList<CompetitorBuyerRowRaw>> GetCompetitorBuyersAsync(
        ResolvedCompetitorQuery query, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);
}
