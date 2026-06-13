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

    /// <summary>
    /// Returns all transactions in status 'pending_fiscalization' with RetryCount &lt; maxRetries
    /// that were created before the given cutoff. Intended for the retry worker (no tenant scope).
    /// </summary>
    Task<IReadOnlyList<PendingFiscalizationDto>> GetPendingFiscalizationAsync(
        int maxRetries = 5,
        int olderThanSeconds = 30,
        CancellationToken ct = default);

    /// <summary>
    /// Re-attempts fiscalization for a single transaction.
    /// Increments RetryCount regardless of outcome.
    /// Sets Status='fiscalization_failed' when RetryCount reaches maxRetries.
    /// Returns (ok=true, fiscalNumber) on success; (ok=false, error) on failure.
    /// </summary>
    Task<FiscalizeResultDto> FiscalizeTransactionAsync(
        Guid transactionId,
        CancellationToken ct = default);
}
