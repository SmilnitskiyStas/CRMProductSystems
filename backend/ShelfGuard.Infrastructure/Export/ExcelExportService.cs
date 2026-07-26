using ClosedXML.Excel;
using ShelfGuard.Application.Common;

namespace ShelfGuard.Infrastructure.Export;

/// <summary>
/// ClosedXML (MIT license — NOT EPPlus, which requires a commercial license for v5+ business
/// use, see the task brief) backed implementation. Builds the whole workbook in memory and
/// returns finished bytes — no server-side temp file, matching the plan's "синхронний стрім
/// прямо в HTTP-відповідь" requirement (the controller does <c>File(bytes, ...)</c> directly).
/// </summary>
public sealed class ExcelExportService : IExcelExportService
{
    public ExcelExportResult Export(ExcelExportRequest request)
    {
        var totalRows = request.Rows.Count;
        var truncated = totalRows > request.MaxRows;
        var rowsToWrite = truncated ? request.Rows.Take(request.MaxRows).ToList() : request.Rows;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SanitizeSheetName(request.SheetName));

        var currentRow = 1;

        for (var col = 0; col < request.Headers.Count; col++)
        {
            var cell = sheet.Cell(currentRow, col + 1);
            cell.Value = SanitizeForSpreadsheet(request.Headers[col]);
            cell.Style.Font.Bold = true;
        }
        currentRow++;

        if (truncated)
        {
            var banner =
                $"Показано перші {rowsToWrite.Count:N0} з {totalRows:N0} рядків — файл обрізано лімітом {request.MaxRows:N0} рядків.";
            var bannerCell = sheet.Cell(currentRow, 1);
            bannerCell.Value = SanitizeForSpreadsheet(banner);
            bannerCell.Style.Font.Bold = true;
            bannerCell.Style.Font.FontColor = XLColor.DarkRed;
            if (request.Headers.Count > 1)
                sheet.Range(currentRow, 1, currentRow, request.Headers.Count).Merge();
            currentRow++;
        }

        foreach (var row in rowsToWrite)
        {
            for (var col = 0; col < row.Count; col++)
                SetCellValue(sheet.Cell(currentRow, col + 1), row[col]);
            currentRow++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExcelExportResult(stream.ToArray(), rowsToWrite.Count, truncated);
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                return; // leave blank — no Blank-value gymnastics needed
            case string s:
                cell.Value = SanitizeForSpreadsheet(s);
                break;
            case bool b:
                cell.Value = b;
                break;
            case int i:
                cell.Value = i;
                break;
            case long l:
                cell.Value = l;
                break;
            case decimal dec:
                cell.Value = dec;
                break;
            case double dbl:
                cell.Value = dbl;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case DateOnly dOnly:
                cell.Value = dOnly.ToDateTime(TimeOnly.MinValue);
                break;
            default:
                cell.Value = SanitizeForSpreadsheet(value.ToString() ?? string.Empty);
                break;
        }
    }

    /// <summary>
    /// TASK-414 (security review TASK-412, finding A — OWASP "CSV/Formula Injection"): every
    /// string this class writes into a cell passes through here — headers, the truncation
    /// banner, and every row value (both the explicit <c>string</c> case above and the
    /// <c>.ToString()</c> fallback for any other type) — one centralized choke point instead of
    /// scattering the check across call sites, so no future field can slip through unguarded.
    ///
    /// Verified via a throwaway ClosedXML 0.105.1 probe (build/save/reload-round-trip +
    /// inspecting the raw OOXML) that <c>IXLCell.Value = someString</c> always produces a
    /// properly Text-typed cell (<c>DataType.Text</c>, no <c>&lt;f&gt;</c> formula element, the
    /// literal text landing verbatim in <c>sharedStrings.xml</c>) — ClosedXML itself never
    /// reinterprets a leading <c>=</c> as a formula. The actual risk this guards against is
    /// downstream: spreadsheet applications (Excel, Google Sheets, LibreOffice Calc) apply their
    /// OWN "does this look like a formula" heuristic when RENDERING a cell's text on open —
    /// independent of the file's declared cell type — and can evaluate content starting with
    /// <c>=</c>/<c>+</c>/<c>-</c>/<c>@</c> as a live formula regardless (this is the well-known,
    /// decades-old class of bug OWASP documents as "CSV/Formula Injection", which affects
    /// genuine .xlsx exports too, not just literal .csv). A leading Tab/CR is included too —
    /// some parsers treat those as significant when deciding where a formula-like run of
    /// characters starts.
    ///
    /// Standard OWASP-documented mitigation, applied here: if the value's first character is
    /// one of the dangerous ones, prefix the whole value with a single apostrophe. Excel treats
    /// a leading <c>'</c> as "the rest of this cell is literal text" — the same convention it
    /// applies when a human types <c>'=foo</c> directly into a cell to stop it becoming a
    /// formula. No behavior change for any normal name/email/phone/product-name value, since
    /// none of them legitimately start with these characters.
    ///
    /// Confirmed (same probe) exactly HOW ClosedXML represents this on save — worth recording
    /// since it's surprising if you only skim the round-tripped cell text: ClosedXML recognizes
    /// the leading apostrophe as the real OOXML "quote prefix" convention, the same thing Excel
    /// itself does when a human types <c>'=foo</c>. It does NOT keep a literal <c>'</c> character
    /// in the stored string — it strips it and instead sets <c>cell.Style.IncludeQuotePrefix</c>
    /// (serialized as <c>quotePrefix="1"</c> on the cell's <c>&lt;xf&gt;</c> in styles.xml); the
    /// shared-string text itself round-trips as the ORIGINAL value (e.g. <c>=cmd|...</c>), not
    /// <c>'=cmd|...</c>. This is the correct, spec-native mechanism (stronger than a raw CSV-
    /// style leftover apostrophe would be) — Excel reads that style flag and renders/treats the
    /// cell as forced literal text regardless of what its content starts with. A test asserting
    /// on cell text alone therefore cannot observe this fix; assert on
    /// <c>cell.Style.IncludeQuotePrefix</c> instead (see ExcelExportServiceTests).
    /// </summary>
    private static string SanitizeForSpreadsheet(string value)
    {
        if (value.Length == 0)
            return value;

        return value[0] switch
        {
            '=' or '+' or '-' or '@' or '\t' or '\r' => "'" + value,
            _ => value,
        };
    }

    /// <summary>Excel sheet names: max 31 chars, no [ ] : * ? / \ characters.</summary>
    private static string SanitizeSheetName(string name)
    {
        char[] invalid = ['[', ']', ':', '*', '?', '/', '\\'];
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        if (cleaned.Length == 0) return "Sheet1";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
