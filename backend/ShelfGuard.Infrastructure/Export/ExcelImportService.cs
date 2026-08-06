using System.IO.Compression;
using ClosedXML.Excel;
using ShelfGuard.Application.Common;

namespace ShelfGuard.Infrastructure.Export;

/// <summary>
/// ClosedXML-backed reader — the import-side mirror of <see cref="ExcelExportService"/>, same
/// package (already a dependency, no new NuGet reference needed per the TASK-472 brief). Reads
/// the FIRST worksheet's used range only, cell-by-cell, using <c>GetString()</c> so numeric/date
/// cells still come back as plain display text (a GUID-looking or phone-looking cell should never
/// be silently coerced into a ClosedXML numeric type before <c>SegmentImportParser</c> ever sees
/// it as a string).
///
/// TASK-474/477/(main-session follow-up): <see cref="ParseXlsx"/> enforces <see cref="ImportLimits"/>
/// at TWO points, not one. TASK-477 originally added only the RowCount()/ColumnCount() check below,
/// before the per-cell <c>GetString()</c> copy loop, on the assumption that loop was where an
/// absurdly large upload's cost actually lived. A follow-up empirical probe (throwaway xUnit test,
/// task log addendum) disproved that: <c>new XLWorkbook(stream)</c> ITSELF already fully
/// materializes every cell into ClosedXML's own object graph — measured at ~38-41 s / ~1.7 GB
/// allocated for a &lt;5 MB-on-disk file (1,048,576 rows — Excel's own hard row ceiling — all
/// sharing one shared-strings-table entry) — entirely before RowCount()/ColumnCount() ever get a
/// chance to run. So <see cref="GuardAgainstOversizedZipEntries"/> now runs FIRST, rejecting on the
/// underlying ZIP container's own per-entry uncompressed size (empirically confirmed cheap: ~0-9 ms
/// even against an 85 MB entry, since it only reads size metadata off the ZIP central directory —
/// no decompression needed). The original RowCount()/ColumnCount() check stays as a second layer,
/// still a valid, cheap bound on the copy loop for anything that passes the first check.
/// </summary>
public sealed class ExcelImportService : IExcelImportService
{
    public ExcelImportResult ParseXlsx(Stream stream)
    {
        MemoryStream? ownedBuffer = null;
        var seekable = stream;
        if (!stream.CanSeek)
        {
            // A raw (e.g. HTTP request body) Stream is not guaranteed to support seeking twice,
            // but the ZIP pre-check below and the XLWorkbook construction that follows each need
            // to read from position 0. Bounded by this interface's own documented 10 MB
            // upload-cap precondition (see IExcelImportService) — the same bytes ClosedXML's own
            // subsequent full read would buffer internally regardless.
            ownedBuffer = new MemoryStream();
            stream.CopyTo(ownedBuffer);
            seekable = ownedBuffer;
        }

        using (ownedBuffer)
        {
            seekable.Position = 0;
            GuardAgainstOversizedZipEntries(seekable);
            seekable.Position = 0;

            using var workbook = new XLWorkbook(seekable);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new FormatException("Workbook has no worksheets.");

            var usedRange = sheet.RangeUsed();
            if (usedRange is null)
                return new ExcelImportResult([]);

            // TASK-474 finding A (second layer): check the bounding-box row/column counts —
            // cheap address arithmetic already available on the range, no per-cell access — and
            // reject BEFORE the loop below calls GetString() on every cell. RowCount() is the
            // bounding box's height (>= the RowsUsed() count the loop actually iterates), so
            // bounding it here is a valid, sufficient upper bound on the loop's own cost — it just
            // no longer has to (and cannot) also cover the constructor's cost above it.
            var rowCount = usedRange.RowCount();
            var columnCount = usedRange.ColumnCount();

            if (rowCount > ImportLimits.MaxRows)
                throw new ImportTooLargeException(
                    $"Workbook has {rowCount:N0} rows, exceeding the {ImportLimits.MaxRows:N0}-row import limit.");
            if (columnCount > ImportLimits.MaxColumns)
                throw new ImportTooLargeException(
                    $"Workbook has {columnCount:N0} columns, exceeding the {ImportLimits.MaxColumns:N0}-column import limit.");

            var rows = new List<IReadOnlyList<string?>>();
            foreach (var row in usedRange.RowsUsed())
            {
                var cells = new List<string?>();
                for (var col = 1; col <= columnCount; col++)
                    cells.Add(row.Cell(col).GetString());
                rows.Add(cells);
            }

            return new ExcelImportResult(rows);
        }
    }

    /// <summary>
    /// TASK-474 finding A, round 2 — runs BEFORE <c>new XLWorkbook(stream)</c>, since that
    /// constructor is where the real cost is (see this class's own doc comment). A .xlsx is a
    /// standard ZIP container; <see cref="ZipArchiveEntry.Length"/> is each entry's UNCOMPRESSED
    /// size, read directly off the ZIP central directory with no decompression required (confirmed
    /// empirically: ~0-9 ms even against an entry that decompresses to 85 MB). That is exactly the
    /// dimension the controller's 10 MB compressed-upload-size cap cannot see, and exactly what an
    /// OOXML shared-strings-table exploit inflates arbitrarily while staying tiny on disk.
    /// </summary>
    private static void GuardAgainstOversizedZipEntries(Stream seekableStream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(seekableStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            // Empirically confirmed (task log addendum): a garbage-bytes or empty stream fails
            // here with InvalidDataException, not a FormatException. Swallow it and fall through
            // to XLWorkbook's own constructor below, which throws the already-tested
            // FileFormatException (a FormatException) for this same input (finding C) — not this
            // guard's concern, since it exists only to bound cost for an otherwise-well-formed ZIP.
            return;
        }

        using (zip)
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > ImportLimits.MaxUncompressedZipEntryBytes)
                    throw new ImportTooLargeException(
                        $"Workbook part '{entry.FullName}' decompresses to {entry.Length:N0} bytes, " +
                        $"exceeding the {ImportLimits.MaxUncompressedZipEntryBytes:N0}-byte per-part limit.");
            }
        }
    }
}
