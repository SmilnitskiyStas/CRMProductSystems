using ShelfGuard.Application.Features.Pos.Dtos;

namespace ShelfGuard.Application.Features.Pos;

public interface IPosService
{
    /// <param name="registerId">
    /// Set for cashier-app (register operator) calls: the shift is bound to that register and
    /// the "already open" check becomes per-register instead of per-tenant. Null = legacy/mobile.
    /// </param>
    Task<(ShiftDto? Shift, string? Error, int? StatusCode)> OpenShiftAsync(
        Guid tenantId, Guid cashierId, OpenShiftRequest request, CancellationToken ct = default,
        Guid? registerId = null);

    Task<ShiftDto?> GetCurrentShiftAsync(Guid tenantId, CancellationToken ct = default, Guid? registerId = null);

    /// <summary>
    /// Closes the current open shift and generates a Z-report via the fiscal provider.
    /// TASK-356: when <paramref name="request"/>.ActualClosingCash is provided, performs
    /// cash-drawer reconciliation — compares it against ExpectedCashAmount (OpeningCash +
    /// this shift's cash-only sales) and returns CashDiscrepancy in the response. Omit (or
    /// pass null) to close without reconciliation, unchanged from prior behavior.
    /// Returns (null, error, statusCode) on business-rule errors:
    ///   404 — no open shift
    ///   400 — ActualClosingCash is negative
    /// </summary>
    Task<(ShiftDto? Shift, string? Error, int? StatusCode)> CloseShiftAsync(
        Guid tenantId, CloseShiftRequest? request = null, CancellationToken ct = default,
        Guid? registerId = null);

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
    /// <remarks>
    /// Idempotent when <c>request.ClientTransactionId</c> is set: a repeat returns the stored
    /// sale. <paramref name="registerId"/> (from the operator JWT) is stamped on the transaction
    /// and must match the shift's register.
    /// </remarks>
    Task<(SaleDto? Sale, string? Error, int? StatusCode)> CreateSaleAsync(
        Guid tenantId, Guid cashierId, CreateSaleRequest request, CancellationToken ct = default,
        Guid? registerId = null);

    /// <summary>
    /// Offline sync: processes each queued sale independently (per-item result; one failure never
    /// aborts the rest). 400 when the batch is empty or larger than 100.
    /// </summary>
    Task<(BatchSalesResultDto? Result, string? Error, int? StatusCode)> CreateSalesBatchAsync(
        Guid tenantId, Guid cashierId, BatchSalesRequest request, CancellationToken ct = default,
        Guid? registerId = null);

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
