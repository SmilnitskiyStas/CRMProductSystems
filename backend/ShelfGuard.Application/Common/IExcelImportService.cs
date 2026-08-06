namespace ShelfGuard.Application.Common;

/// <summary>
/// Generic .xlsx → rows-of-strings reader — the import-side mirror of
/// <see cref="IExcelExportService"/>. Reads the first worksheet only; every cell is returned as
/// its display text so callers (e.g. Фаза 4 PostCampaign's column auto-detection, TASK-472) never
/// need a ClosedXML dependency themselves — same reason <see cref="IExcelExportService"/> keeps
/// ClosedXML entirely out of the Application layer (<c>ShelfGuard.Application.csproj</c> carries
/// no ClosedXML package reference; only <c>ShelfGuard.Infrastructure</c> does). Deliberately
/// generic — no PostCampaign-specific shape here — so any future feature needing "let the user
/// upload an .xlsx" can reuse this instead of a second ClosedXML entrypoint.
/// </summary>
public interface IExcelImportService
{
    /// <summary>
    /// Parses every used row of the first worksheet. Row 0 is not treated specially here (no
    /// header/data distinction) — that interpretation is entirely the caller's business logic;
    /// this method only turns bytes into a rectangular grid of cell text. Throws
    /// <see cref="FormatException"/> if the stream is not a readable .xlsx workbook, and
    /// <see cref="ImportTooLargeException"/> if EITHER the underlying ZIP container's own
    /// per-entry uncompressed size exceeds <see cref="ImportLimits.MaxUncompressedZipEntryBytes"/>
    /// (checked BEFORE ClosedXML's <c>XLWorkbook</c> constructor runs at all — empirically
    /// confirmed to be where a maliciously compressible upload's real cost lands, not merely the
    /// per-cell copy loop; see the implementation's own doc comment and the task log) OR the
    /// workbook's row/column count exceeds <see cref="ImportLimits"/> (checked after construction,
    /// before the per-cell copy into the returned grid runs — TASK-474 finding A's original,
    /// still-valid second layer). Callers should catch both exception types and map them to a
    /// clean 400 rather than letting either propagate.
    /// </summary>
    ExcelImportResult ParseXlsx(Stream stream);
}

public sealed record ExcelImportResult(IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>
/// Thrown by <see cref="IExcelImportService.ParseXlsx"/> — and, for defense-in-depth, by
/// <c>SegmentImportParser</c>'s text/CSV parsers too — when the input exceeds
/// <see cref="ImportLimits"/> BEFORE it is fully materialized or classified.
///
/// TASK-474 finding A (security review of Фаза 4 post-campaign import): ClosedXML's
/// <c>XLWorkbook</c> is not a streaming reader — a small (10 MB, correctly enforced at the
/// controller), highly-compressible .xlsx exploiting OOXML's shared-strings-table can expand into
/// a very large in-memory object graph before any caller's own business-level cap (e.g.
/// <c>PostCampaignService.MaxAcceptedRows</c> = 20,000) ever gets a chance to run. TASK-477
/// originally addressed this by reading row/column counts off the already-parsed range and
/// rejecting before copying every cell into a <c>List&lt;List&lt;string?&gt;&gt;</c> — but a
/// follow-up empirical probe (task log addendum) proved that guard alone is NOT early enough: the
/// expensive full materialization happens inside the <c>XLWorkbook</c> CONSTRUCTOR itself
/// (measured ~38-41 s / ~1.7 GB allocated for a &lt;5 MB attack file), before RowCount()/
/// ColumnCount() can even run. <see cref="IExcelImportService.ParseXlsx"/> now also checks the
/// underlying ZIP container's own per-entry uncompressed size — see
/// <see cref="ImportLimits.MaxUncompressedZipEntryBytes"/> — BEFORE that constructor runs at all;
/// the row/column check remains as a second layer for anything that passes.
///
/// <see cref="ImportLimits"/>'s row/column ceilings are deliberately more generous than any known
/// caller's own business cap, so a legitimate submission only a bit over THAT caller's specific
/// limit still reaches the caller's own friendlier, specific error instead of this generic one —
/// this exception exists only to bound worst-case cost for input that is drastically oversized.
/// Callers should catch it and map it to a clean 400, the same way <see cref="FormatException"/>
/// is already handled for an unreadable workbook.
/// </summary>
public sealed class ImportTooLargeException : Exception
{
    public ImportTooLargeException(string message) : base(message)
    {
    }
}
