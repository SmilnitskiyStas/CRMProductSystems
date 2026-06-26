using System.Globalization;
using ShelfGuard.Application.Features.Sales.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Sales;

public sealed class DailySalesService : IDailySalesService
{
    private const int MaxCsvRows = 10_000;

    private readonly IDailySalesRepository _repo;

    public DailySalesService(IDailySalesRepository repo) => _repo = repo;

    public async Task<List<DailySaleDto>> GetAsync(
        Guid? storeId, Guid? productId, DateOnly? from, DateOnly? to,
        CancellationToken ct = default)
    {
        var sales = await _repo.GetAsync(storeId, productId, from, to, ct);
        return sales.Select(ToDto).ToList();
    }

    public async Task<(DailySaleDto? Sale, string? Error)> UpsertAsync(
        Guid tenantId, UpsertDailySaleRequest request, CancellationToken ct = default)
    {
        var error = Validate(request.QuantitySold, request.QuantityEndOfDay, request.Date);
        if (error is not null) return (null, error);

        if (!await _repo.StoreExistsAsync(request.StoreId, ct))
            return (null, "Store not found.");
        if (!await _repo.ProductExistsAsync(request.ProductId, ct))
            return (null, "Product not found.");

        var existing = await _repo.FindAsync(request.StoreId, request.ProductId, request.Date, ct);
        DailySale sale;

        if (existing is null)
        {
            sale = new DailySale
            {
                TenantId = tenantId,
                StoreId = request.StoreId,
                ProductId = request.ProductId,
                Date = request.Date,
                QuantitySold = request.QuantitySold,
                QuantityEndOfDay = request.QuantityEndOfDay,
                IsPromoDay = request.IsPromoDay,
                Source = "manual",
            };
            await _repo.AddAsync(sale, ct);
        }
        else
        {
            existing.QuantitySold = request.QuantitySold;
            existing.QuantityEndOfDay = request.QuantityEndOfDay;
            existing.IsPromoDay = request.IsPromoDay;
            existing.Source = "manual";
            sale = existing;
        }

        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(sale.Id, ct);
        return (ToDto(saved!), null);
    }

    public async Task<(DailySaleDto? Sale, string? Error)> MarkAnomalyAsync(
        Guid id, bool isAnomaly, CancellationToken ct = default)
    {
        var sale = await _repo.GetByIdAsync(id, ct);
        if (sale is null)
            return (null, "Daily sale not found.");

        sale.IsAnomaly = isAnomaly;
        await _repo.SaveChangesAsync(ct);
        return (ToDto(sale), null);
    }

    public async Task<(CsvImportResult? Result, string? Error)> ImportCsvAsync(
        Guid tenantId, Guid storeId, string csvContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
            return (null, "CSV content is empty.");

        if (!await _repo.StoreExistsAsync(storeId, ct))
            return (null, "Store not found.");

        var lines = csvContent
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
            return (null, "CSV must contain a header row and at least one data row.");
        if (lines.Count - 1 > MaxCsvRows)
            return (null, $"CSV exceeds the {MaxCsvRows} row limit.");

        var rows = ParseRows(lines, out var parseErrors);

        // Resolve all barcodes in one query
        var barcodes = rows.Select(r => r.Barcode).Distinct().ToList();
        var productMap = await _repo.GetProductIdsByBarcodesAsync(barcodes, ct);

        int created = 0, updated = 0, skipped = parseErrors.Count;
        var errors = parseErrors;

        foreach (var row in rows)
        {
            if (!productMap.TryGetValue(row.Barcode, out var productId))
            {
                skipped++;
                errors.Add($"Line {row.LineNumber}: barcode '{row.Barcode}' not found in catalog.");
                continue;
            }

            var existing = await _repo.FindAsync(storeId, productId, row.Date, ct);
            if (existing is null)
            {
                await _repo.AddAsync(new DailySale
                {
                    TenantId = tenantId,
                    StoreId = storeId,
                    ProductId = productId,
                    Date = row.Date,
                    QuantitySold = row.QuantitySold,
                    QuantityEndOfDay = row.QuantityEndOfDay,
                    IsPromoDay = row.IsPromoDay,
                    Source = "import",
                }, ct);
                created++;
            }
            else
            {
                existing.QuantitySold = row.QuantitySold;
                existing.QuantityEndOfDay = row.QuantityEndOfDay;
                existing.IsPromoDay = row.IsPromoDay;
                existing.Source = "import";
                updated++;
            }
        }

        await _repo.SaveChangesAsync(ct);
        return (new CsvImportResult(created, updated, skipped, errors), null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string? Validate(decimal quantitySold, decimal? quantityEndOfDay, DateOnly date)
    {
        if (quantitySold < 0)
            return "QuantitySold must be >= 0.";
        if (quantityEndOfDay is < 0)
            return "QuantityEndOfDay must be >= 0.";
        if (date > DateOnly.FromDateTime(DateTime.UtcNow))
            return "Date cannot be in the future.";
        return null;
    }

    internal sealed record CsvRow(
        int LineNumber, string Barcode, DateOnly Date,
        decimal QuantitySold, decimal? QuantityEndOfDay, bool IsPromoDay);

    /// <summary>
    /// Parses data lines. Expected header: barcode,date,quantity_sold[,quantity_end_of_day][,is_promo_day].
    /// Internal+static so unit tests can exercise parsing without a repository.
    /// </summary>
    internal static List<CsvRow> ParseRows(List<string> lines, out List<string> errors)
    {
        errors = [];
        var rows = new List<CsvRow>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var i = 1; i < lines.Count; i++)
        {
            var lineNo = i + 1;
            var parts = lines[i].Split(',');

            if (parts.Length < 3)
            {
                errors.Add($"Line {lineNo}: expected at least 3 columns (barcode,date,quantity_sold).");
                continue;
            }

            var barcode = parts[0].Trim();
            if (barcode.Length == 0)
            {
                errors.Add($"Line {lineNo}: barcode is empty.");
                continue;
            }

            if (!DateOnly.TryParseExact(parts[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                errors.Add($"Line {lineNo}: invalid date '{parts[1].Trim()}' (expected yyyy-MM-dd).");
                continue;
            }
            if (date > today)
            {
                errors.Add($"Line {lineNo}: date {date:yyyy-MM-dd} is in the future.");
                continue;
            }

            if (!decimal.TryParse(parts[2].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                    out var qtySold) || qtySold < 0)
            {
                errors.Add($"Line {lineNo}: invalid quantity_sold '{parts[2].Trim()}'.");
                continue;
            }

            decimal? qtyEod = null;
            if (parts.Length >= 4 && parts[3].Trim().Length > 0)
            {
                if (!decimal.TryParse(parts[3].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                        out var eod) || eod < 0)
                {
                    errors.Add($"Line {lineNo}: invalid quantity_end_of_day '{parts[3].Trim()}'.");
                    continue;
                }
                qtyEod = eod;
            }

            var isPromo = parts.Length >= 5 && parts[4].Trim().Length > 0
                && (parts[4].Trim() == "1" || parts[4].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

            rows.Add(new CsvRow(lineNo, barcode, date, qtySold, qtyEod, isPromo));
        }

        return rows;
    }

    private static DailySaleDto ToDto(DailySale s) => new(
        s.Id,
        s.StoreId,
        s.Store?.Name ?? "",
        s.ProductId,
        s.Product?.Name ?? "",
        s.Product?.Barcodes.Count > 0 ? s.Product.Barcodes[0] : null,
        s.Date,
        s.QuantitySold,
        s.QuantityEndOfDay,
        s.IsPromoDay,
        s.IsAnomaly,
        s.Source
    );
}
