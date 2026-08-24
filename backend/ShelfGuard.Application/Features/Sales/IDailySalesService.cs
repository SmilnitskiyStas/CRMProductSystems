using ShelfGuard.Application.Features.Sales.Dtos;

namespace ShelfGuard.Application.Features.Sales;

public interface IDailySalesService
{
    /// <summary>
    /// TASK-610: storeIds is a repeated query param — omitted or empty means "all stores".
    /// </summary>
    Task<List<DailySaleDto>> GetAsync(
        Guid[]? storeIds,
        Guid? productId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    /// <summary>Creates or updates the (store, product, date) row. Source = manual.</summary>
    Task<(DailySaleDto? Sale, string? Error)> UpsertAsync(
        Guid tenantId,
        UpsertDailySaleRequest request,
        CancellationToken ct = default);

    Task<(DailySaleDto? Sale, string? Error)> MarkAnomalyAsync(
        Guid id,
        bool isAnomaly,
        CancellationToken ct = default);

    /// <summary>
    /// CSV import: header "barcode,date,quantity_sold[,quantity_end_of_day][,is_promo_day]".
    /// Rows upsert by (store, product, date). Source = import.
    /// </summary>
    Task<(CsvImportResult? Result, string? Error)> ImportCsvAsync(
        Guid tenantId,
        Guid storeId,
        string csvContent,
        CancellationToken ct = default);
}
