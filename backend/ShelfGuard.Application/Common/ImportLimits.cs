namespace ShelfGuard.Application.Common;

/// <summary>
/// Shared defensive ceilings for any "user uploads a file or pastes a list of rows" import path —
/// XLSX (<see cref="IExcelImportService"/>) and CSV/raw-text (<c>SegmentImportParser</c>) alike.
///
/// TASK-474 finding A (security review): exists purely to bound worst-case memory/CPU cost BEFORE
/// a caller's own business-level cap (e.g. <c>PostCampaignService.MaxAcceptedRows</c> = 20,000,
/// the only real caller today) ever gets a chance to run. Deliberately more generous than that
/// real business cap — about 1.25x it for rows — so a submission only a bit over the real 20,000
/// limit still reaches that caller's own friendlier, specific "20,000 max" error instead of the
/// generic <see cref="ImportTooLargeException"/> these ceilings throw; only input that is
/// drastically oversized (the actual resource-exhaustion shape finding A describes) hits this
/// instead. A plain shared constant rather than something wired to any one feature's own cap
/// directly, since <see cref="IExcelImportService"/> is deliberately generic, reusable
/// infrastructure with no knowledge of PostCampaign or any other specific caller — re-tune if a
/// second caller ever needs a materially different business cap.
/// </summary>
public static class ImportLimits
{
    /// <summary>Row ceiling, including any header row.</summary>
    public const int MaxRows = 25_000;

    /// <summary>
    /// Column ceiling — a workbook that is small in row count but absurdly wide hits the same
    /// per-cell amplification shape as too many rows (see <see cref="IExcelImportService"/>'s
    /// row-by-column copy loop). No real caller today needs more than a handful of columns; a
    /// few hundred is already generous headroom. Checked only for XLSX: CSV/raw-text parsing
    /// never does a rows-by-columns nested materialization (each row only ever yields ONE
    /// selected token column downstream), so an equivalent column cap would guard nothing there —
    /// row/token count is that path's only real cost dimension, already covered below.
    /// </summary>
    public const int MaxColumns = 300;

    /// <summary>
    /// TASK-474 finding A, round 2 (main-session empirical follow-up to TASK-477) — per-ZIP-entry
    /// ceiling on a .xlsx part's UNCOMPRESSED size, checked BEFORE ClosedXML's <c>XLWorkbook</c>
    /// constructor ever runs. <see cref="MaxRows"/>/<see cref="MaxColumns"/> above are checked
    /// AFTER that constructor returns — TASK-477 assumed that was early enough ("the row-by-column
    /// GetString() copy loop... is where an absurdly large/wide upload's cost was previously
    /// unbounded"), but a follow-up empirical probe (throwaway xUnit test, task log addendum)
    /// proved the constructor itself already does the full, expensive per-cell materialization the
    /// row/column guard was meant to prevent — that cost lands entirely before the guard can run
    /// at all. Measured on real synthetic .xlsx files (OOXML zip-bomb shape: every cell shares ONE
    /// shared-strings-table entry, so the file stays tiny on disk regardless of row count):
    /// 25,000 rows (today's <see cref="MaxRows"/>) → ~2.0 MB uncompressed, ctor ~0.4 s; 250,000
    /// rows → ~20.1 MB uncompressed, ctor ~4.9 s / ~411 MB allocated; 1,048,576 rows (Excel's own
    /// hard row ceiling — the true worst case for a single column) → ~85-89 MB uncompressed, ctor
    /// ~38-41 s / ~1.7 GB allocated — all in a &lt;5 MB file, comfortably under the controller's
    /// 10 MB upload cap. This ceiling is deliberately sized well above any known legitimate shape
    /// (the only real caller, <c>PostCampaignService</c>, needs a handful of columns at well under
    /// the <see cref="MaxRows"/> ceiling — a realistic worst-case legitimate workbook at that shape
    /// lands in the low single-digit MB uncompressed, per the same measurement) while giving a
    /// firm, bounded worst case for anything that does reach this check — re-tune if a future
    /// caller genuinely needs a wider/denser legitimate workbook, same as <see cref="MaxColumns"/>
    /// above.
    /// </summary>
    public const long MaxUncompressedZipEntryBytes = 20L * 1024 * 1024;
}
