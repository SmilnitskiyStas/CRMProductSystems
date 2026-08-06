using System.Text;
using ClosedXML.Excel;
using ShelfGuard.Application.Common;
using ShelfGuard.Infrastructure.Export;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-477 (security review TASK-474, finding A/C) plus a main-session empirical follow-up to
/// finding A: regression coverage for <see cref="ExcelImportService.ParseXlsx"/>'s defensive
/// row/column ceiling, its (later-added) ZIP-entry uncompressed-size ceiling — added after an
/// empirical probe proved the row/column check alone runs too late to bound
/// <c>new XLWorkbook(stream)</c>'s own cost, see the task log addendum — its non-seekable-stream
/// handling, and its behavior on malformed input. The two malformed-input exception types below
/// (<see cref="FormatException"/>'s own <see cref="System.IO.FileFormatException"/> subtype, and a
/// bare <see cref="NullReferenceException"/>) were empirically confirmed against the actual
/// ClosedXML 0.105.1 behavior for this codebase (a throwaway probe, same verification discipline
/// TASK-414 used for the ClosedXML quote-prefix behavior) — not assumed from documentation, since
/// ClosedXML itself throws neither type directly (both originate one layer down, in the
/// DocumentFormat.OpenXml/System.IO.Packaging package-reading layer ClosedXML's constructor calls
/// into) and nothing in ClosedXML's own public contract documents this.
/// </summary>
public sealed class ExcelImportServiceTests
{
    private readonly ExcelImportService _sut = new();

    // ── Row/column ceiling (finding A) ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseXlsx_rejects_a_workbook_whose_row_bounding_box_exceeds_the_ceiling()
    {
        // Only 2 cells are actually written — row 1 and one far past the ceiling — so this stays
        // fast while still proving the ceiling is enforced off the RANGE's bounding-box RowCount()
        // (cheap address arithmetic), not by first counting/copying every populated cell.
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "id";
        sheet.Cell(ImportLimits.MaxRows + 1, 1).Value = "last";
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var ex = Assert.Throws<ImportTooLargeException>(() => _sut.ParseXlsx(ms));
        Assert.Contains(ImportLimits.MaxRows.ToString("N0"), ex.Message);
    }

    [Fact]
    public void ParseXlsx_rejects_a_workbook_whose_column_bounding_box_exceeds_the_ceiling()
    {
        // Small in row count (2 rows) but absurdly wide — the same amplification shape along the
        // other axis; must be rejected even though the row ceiling alone would not catch it.
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "id";
        sheet.Cell(1, ImportLimits.MaxColumns + 1).Value = "last";
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var ex = Assert.Throws<ImportTooLargeException>(() => _sut.ParseXlsx(ms));
        Assert.Contains(ImportLimits.MaxColumns.ToString("N0"), ex.Message);
    }

    [Fact]
    public void ParseXlsx_accepts_a_workbook_within_both_ceilings()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "id";
        sheet.Cell(1, 2).Value = "phone";
        sheet.Cell(2, 1).Value = "11111111-1111-1111-1111-111111111111";
        sheet.Cell(2, 2).Value = "+380671234567";
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var result = _sut.ParseXlsx(ms);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("id", result.Rows[0][0]);
        Assert.Equal("+380671234567", result.Rows[1][1]);
    }

    // ── ZIP-entry uncompressed-size ceiling (finding A, round 2) ────────────────────────────────
    // Main-session empirical follow-up to TASK-477: new XLWorkbook(stream) alone (before this
    // class's RowCount()/ColumnCount() check ever runs) was measured to cost ~38-41s / ~1.7GB for
    // a <5MB-on-disk file exploiting OOXML's shared-strings table (task log addendum has the full
    // numbers). ExcelImportService now checks each ZIP entry's own uncompressed size BEFORE the
    // XLWorkbook constructor runs at all.

    [Fact]
    public void ParseXlsx_rejects_a_zip_entry_whose_uncompressed_size_exceeds_the_ceiling()
    {
        // A well-formed ZIP — not even a valid xlsx package — with ONE entry whose real
        // (uncompressed) content exceeds the ceiling. Content is a single repeated byte so it
        // compresses to almost nothing and this test stays fast despite the entry declaring a
        // >20 MB uncompressed size. Not shaped like a real xlsx on purpose: proves the new guard
        // fires (and pre-empts ClosedXML entirely) rather than merely coincidentally overlapping
        // with the "well-formed zip that is not a valid xlsx package" case below (which — proven
        // by that test — ClosedXML itself rejects with a totally different exception,
        // NullReferenceException, if this guard did not intercept first).
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using var entryStream = entry.Open();
            var oversized = new byte[ImportLimits.MaxUncompressedZipEntryBytes + 1];
            Array.Fill(oversized, (byte)'a');
            entryStream.Write(oversized, 0, oversized.Length);
        }
        ms.Position = 0;

        var ex = Assert.Throws<ImportTooLargeException>(() => _sut.ParseXlsx(ms));
        Assert.Contains(ImportLimits.MaxUncompressedZipEntryBytes.ToString("N0"), ex.Message);
    }

    // ── Non-seekable input stream ───────────────────────────────────────────────────────────────
    // TASK-474 finding A round 2's explicit ask: a raw (e.g. HTTP request body) Stream is not
    // guaranteed to support seeking twice, since ParseXlsx now needs to read the same bytes once
    // for the ZIP pre-check and again for XLWorkbook. Proves the CanSeek-false branch buffers
    // once and both reads still succeed against the same content.

    [Fact]
    public void ParseXlsx_parses_a_non_seekable_stream_by_buffering_it_first()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "id";
        sheet.Cell(2, 1).Value = "11111111-1111-1111-1111-111111111111";
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);

        using var nonSeekable = new NonSeekableReadOnlyStream(ms.ToArray());

        var result = _sut.ParseXlsx(nonSeekable);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result.Rows[1][0]);
    }

    private sealed class NonSeekableReadOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // ── Malformed input (finding C) ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseXlsx_throws_a_FormatException_for_garbage_bytes()
    {
        // Empirically confirmed: surfaces as System.IO.FileFormatException ("File contains
        // corrupted data."), which IS a FormatException (ThrowsAny accepts the subtype) — this is
        // also the realistic shape of a .txt renamed to .xlsx, the review's explicit example.
        var bytes = Encoding.UTF8.GetBytes("not a real xlsx file, just plain text");

        Assert.ThrowsAny<FormatException>(() => _sut.ParseXlsx(new MemoryStream(bytes)));
    }

    [Fact]
    public void ParseXlsx_throws_a_FormatException_for_an_empty_stream()
    {
        // Empirically confirmed: System.IO.FileFormatException ("Archive file cannot be size 0.").
        Assert.ThrowsAny<FormatException>(() => _sut.ParseXlsx(new MemoryStream()));
    }

    [Fact]
    public void ParseXlsx_throws_for_a_well_formed_zip_that_is_not_a_valid_xlsx_package()
    {
        // A genuine zip archive (so it passes the outer "is this a zip" check) that lacks the
        // OOXML parts a spreadsheet package requires. Empirically confirmed: ClosedXML's own
        // LoadSpreadsheetDocument throws a bare NullReferenceException for this shape — a real,
        // if unfortunate, ClosedXML 0.105.1 behavior, not a defect introduced by this codebase.
        // PostCampaignService.ImportAsync's catch clause explicitly also catches this type (see
        // PostCampaignServiceTests) so this is asserted here as a pinned, known contract rather
        // than left undocumented.
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("hello.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello world");
        }
        ms.Position = 0;

        Assert.Throws<NullReferenceException>(() => _sut.ParseXlsx(ms));
    }
}
