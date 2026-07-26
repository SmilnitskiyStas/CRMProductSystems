namespace ShelfGuard.Application.Common;

/// <summary>
/// Generic Excel (.xlsx) generation, introduced for marketing-analytics exports (TASK-406)
/// but deliberately generic — no MarketingAnalytics-specific shape here — so any future
/// feature can reuse it instead of a second implementation. ClosedXML (MIT) builds the
/// workbook fully in memory and returns finished bytes; the caller streams them straight into
/// the HTTP response (<c>File(bytes, ...)</c>) — no server-side temp file ever touches disk.
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Builds a single-sheet workbook. The caller passes ALL rows it has, uncapped — this
    /// method owns the 50k-row policy itself: rows beyond <see cref="ExcelExportRequest.MaxRows"/>
    /// are dropped and a visible banner row ("Показано перші N з M рядків...") is inserted right
    /// after the header, with <see cref="ExcelExportResult.Truncated"/> set. Callers never need
    /// to duplicate this truncation logic themselves.
    /// </summary>
    ExcelExportResult Export(ExcelExportRequest request);
}

public sealed record ExcelExportRequest(
    string SheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int MaxRows = 50_000);

public sealed record ExcelExportResult(byte[] FileBytes, int RowCount, bool Truncated);
