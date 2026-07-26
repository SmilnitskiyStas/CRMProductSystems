using ClosedXML.Excel;
using ShelfGuard.Application.Common;
using ShelfGuard.Infrastructure.Export;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-414 (security review TASK-412, finding A — CRITICAL): regression coverage for the
/// OWASP "CSV/Formula Injection" fix in <see cref="ExcelExportService"/>. This is the only
/// production code path that turns arbitrary caller-supplied strings into .xlsx cell content —
/// including values that can originate from fully anonymous public input (e.g.
/// <c>ConsumerAccount.FullName</c> via <c>POST /api/consumer-auth/register</c>, TASK-405, which
/// flows into <c>Customer.Name</c> and from there into marketing-analytics exports). Every
/// assertion opens the ACTUAL produced bytes with ClosedXML (a real round-trip, not just
/// inspecting what was passed in).
///
/// IMPORTANT (verified via a throwaway ClosedXML 0.105.1 probe, see ExcelExportService.
/// SanitizeForSpreadsheet's doc comment): a leading apostrophe does NOT survive as a literal
/// character in the round-tripped cell text — ClosedXML implements the real OOXML "quote
/// prefix" convention, stripping the apostrophe and instead setting
/// <see cref="IXLStyle.IncludeQuotePrefix"/> (serialized as <c>quotePrefix="1"</c> in
/// styles.xml). So the fix is observed via that style flag, not via the cell's text gaining a
/// leading <c>'</c>.
/// </summary>
public sealed class ExcelExportServiceTests
{
    private readonly ExcelExportService _sut = new();

    // The exact payload from the security review's brief, plus the +/-/@ variants it explicitly
    // asked for, plus Tab — the one other OWASP-listed character that survives an XML
    // round-trip unchanged (a lone leading CR gets normalized to LF by the XML spec itself on
    // any save/reload, independent of this fix, so it's covered separately below).
    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("\t=tabprefixed")]
    public void Export_dangerous_leading_character_is_quote_prefixed_not_a_formula(string malicious)
    {
        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["Name"], [new object?[] { malicious }]));

        var cell = ReadCell(result.FileBytes, row: 2, col: 1);

        // ClosedXML strips the apostrophe our sanitizer adds and represents it the same way
        // Excel itself does for a manually quote-prefixed cell — text round-trips as the
        // original value, protection lives in the style flag, not the text.
        Assert.Equal(malicious, cell.Text);
        Assert.True(cell.IncludeQuotePrefix, "expected the cell to carry the OOXML quote-prefix flag");
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
    }

    [Fact]
    public void Export_leading_CR_is_quote_prefixed_even_though_xml_normalizes_the_character_itself()
    {
        var malicious = "\r=crprefixed";

        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["Name"], [new object?[] { malicious }]));

        var cell = ReadCell(result.FileBytes, row: 2, col: 1);

        // The XML 1.0 spec itself normalizes a lone CR to LF on any parse — unrelated to this
        // fix — so compare against that normalized form rather than the raw literal.
        Assert.Equal("\n=crprefixed", cell.Text);
        Assert.True(cell.IncludeQuotePrefix, "expected the cell to carry the OOXML quote-prefix flag");
        Assert.False(cell.HasFormula);
    }

    [Theory]
    [InlineData("Ivan Petrenko")]
    [InlineData("Іван Петренко")]
    [InlineData("ivan@example.com")]
    [InlineData("Bananas (500g)")]
    public void Export_normal_values_are_written_unchanged_and_not_quote_prefixed(string benign)
    {
        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["Name"], [new object?[] { benign }]));

        var cell = ReadCell(result.FileBytes, row: 2, col: 1);

        Assert.Equal(benign, cell.Text);
        Assert.False(cell.IncludeQuotePrefix);
        Assert.False(cell.HasFormula);
    }

    [Fact]
    public void Export_sanitizes_header_row_too()
    {
        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["=EVIL()"], [new object?[] { "row" }]));

        var header = ReadCell(result.FileBytes, row: 1, col: 1);

        Assert.Equal("=EVIL()", header.Text);
        Assert.True(header.IncludeQuotePrefix);
        Assert.False(header.HasFormula);
    }

    [Fact]
    public void Export_non_string_values_pass_through_untouched()
    {
        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["N"], [new object?[] { 42 }]));

        var cell = ReadCell(result.FileBytes, row: 2, col: 1);

        Assert.Equal("42", cell.Text);
        Assert.False(cell.IncludeQuotePrefix);
        Assert.False(cell.HasFormula);
    }

    [Fact]
    public void Export_multiple_columns_quote_prefixes_only_the_dangerous_one()
    {
        var result = _sut.Export(new ExcelExportRequest(
            "RFM", ["Name", "Phone"],
            [new object?[] { "=HYPERLINK(\"http://evil.example\")", "+380671234567" }]));

        var name = ReadCell(result.FileBytes, row: 2, col: 1);
        var phone = ReadCell(result.FileBytes, row: 2, col: 2);

        Assert.Equal("=HYPERLINK(\"http://evil.example\")", name.Text);
        Assert.True(name.IncludeQuotePrefix);
        Assert.False(name.HasFormula);

        // A phone number legitimately starts with '+' — also dangerous per OWASP guidance, so
        // it gets the same quote-prefix protection.
        Assert.Equal("+380671234567", phone.Text);
        Assert.True(phone.IncludeQuotePrefix);
        Assert.False(phone.HasFormula);
    }

    private static CellSnapshot ReadCell(byte[] fileBytes, int row, int col)
    {
        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);
        var cell = workbook.Worksheet(1).Cell(row, col);
        return new CellSnapshot(cell.GetString(), cell.HasFormula, cell.DataType, cell.Style.IncludeQuotePrefix);
    }

    private sealed record CellSnapshot(string Text, bool HasFormula, XLDataType DataType, bool IncludeQuotePrefix);
}
