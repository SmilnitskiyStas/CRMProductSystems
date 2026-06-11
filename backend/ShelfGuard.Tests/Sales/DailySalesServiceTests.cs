using ShelfGuard.Application.Features.Sales;
using Xunit;

namespace ShelfGuard.Tests.Sales;

public sealed class DailySalesCsvParsingTests
{
    private static List<string> Lines(params string[] lines) => [.. lines];

    [Fact]
    public void ParseRows_parses_minimal_three_column_rows()
    {
        var rows = DailySalesService.ParseRows(
            Lines("barcode,date,quantity_sold",
                  "4820000001234,2026-06-10,12.5",
                  "4820000005678,2026-06-09,3"),
            out var errors);

        Assert.Empty(errors);
        Assert.Equal(2, rows.Count);
        Assert.Equal("4820000001234", rows[0].Barcode);
        Assert.Equal(new DateOnly(2026, 6, 10), rows[0].Date);
        Assert.Equal(12.5m, rows[0].QuantitySold);
        Assert.Null(rows[0].QuantityEndOfDay);
        Assert.False(rows[0].IsPromoDay);
    }

    [Fact]
    public void ParseRows_parses_optional_columns()
    {
        var rows = DailySalesService.ParseRows(
            Lines("barcode,date,quantity_sold,quantity_end_of_day,is_promo_day",
                  "4820000001234,2026-06-10,12.5,40,1",
                  "4820000005678,2026-06-10,7,,true"),
            out var errors);

        Assert.Empty(errors);
        Assert.Equal(40m, rows[0].QuantityEndOfDay);
        Assert.True(rows[0].IsPromoDay);
        Assert.Null(rows[1].QuantityEndOfDay);
        Assert.True(rows[1].IsPromoDay);
    }

    [Fact]
    public void ParseRows_rejects_bad_rows_and_keeps_good_ones()
    {
        var rows = DailySalesService.ParseRows(
            Lines("barcode,date,quantity_sold",
                  "4820000001234,2026-06-10,12.5",   // ok
                  "4820000005678,10.06.2026,3",      // bad date format
                  ",2026-06-10,5",                   // empty barcode
                  "4820000009999,2026-06-10,-2",     // negative qty
                  "4820000009999,2026-06-10"),       // too few columns
            out var errors);

        Assert.Single(rows);
        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Contains("invalid date"));
        Assert.Contains(errors, e => e.Contains("barcode is empty"));
        Assert.Contains(errors, e => e.Contains("invalid quantity_sold"));
        Assert.Contains(errors, e => e.Contains("at least 3 columns"));
    }

    [Fact]
    public void ParseRows_rejects_future_dates()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)).ToString("yyyy-MM-dd");
        var rows = DailySalesService.ParseRows(
            Lines("barcode,date,quantity_sold", $"4820000001234,{future},5"),
            out var errors);

        Assert.Empty(rows);
        Assert.Single(errors);
        Assert.Contains("in the future", errors[0]);
    }

    [Fact]
    public void ParseRows_reports_correct_line_numbers()
    {
        var rows = DailySalesService.ParseRows(
            Lines("barcode,date,quantity_sold",
                  "4820000001234,2026-06-10,1",
                  "bad-row,not-a-date,1"),
            out var errors);

        Assert.Single(rows);
        Assert.Equal(2, rows[0].LineNumber);
        Assert.Contains("Line 3:", errors[0]);
    }
}
