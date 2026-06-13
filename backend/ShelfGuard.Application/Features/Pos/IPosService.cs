using ShelfGuard.Application.Features.Pos.Dtos;

namespace ShelfGuard.Application.Features.Pos;

public interface IPosService
{
    Task<(ShiftDto? Shift, string? Error, int? StatusCode)> OpenShiftAsync(
        Guid tenantId, Guid cashierId, OpenShiftRequest request, CancellationToken ct = default);

    Task<ShiftDto?> GetCurrentShiftAsync(Guid tenantId, CancellationToken ct = default);

    Task<(ShiftDto? Shift, string? Error, int? StatusCode)> CloseShiftAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Creates a sale (DB tx: FEFO write-down + stock_events + pos_transaction + items).
    /// Fiscalization runs async after commit and never blocks the sale.
    /// Returns (null, error, statusCode) on business-rule errors:
    ///   409 — shift not found or not open
    ///   423 — any item is fully expired
    ///   400 — validation (insufficient stock, barcode not found, etc.)
    /// </summary>
    /// <summary>
    /// StoreId is resolved from the shift (not from JWT), so no storeId parameter.
    /// </summary>
    Task<(SaleDto? Sale, string? Error, int? StatusCode)> CreateSaleAsync(
        Guid tenantId, Guid cashierId, CreateSaleRequest request, CancellationToken ct = default);

    Task<SalesListDto> GetSalesForShiftAsync(Guid tenantId, Guid shiftId, CancellationToken ct = default);
}
