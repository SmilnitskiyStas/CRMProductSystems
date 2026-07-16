namespace ShelfGuard.Application.Features.Pos.Dtos;

// ── Request DTOs ───────────────────────────────────────────────────────────

public sealed record OpenShiftRequest(
    Guid StoreId,
    decimal? OpeningCash = null);

/// <summary>
/// TASK-356: cash-drawer reconciliation at shift close. Optional for backward
/// compatibility — existing clients (mobile app) that send no body at all still close
/// the shift exactly as before, with no reconciliation performed.
/// </summary>
/// <param name="ActualClosingCash">Cash counted by the cashier at close. Must be &gt;= 0
/// when provided. Compared against ExpectedCashAmount (OpeningCash + cash-only sales for
/// the shift) to compute CashDiscrepancy in the response.</param>
public sealed record CloseShiftRequest(decimal? ActualClosingCash = null);

public sealed record CreateSaleRequest(
    Guid ShiftId,
    IReadOnlyList<SaleItemRequest> Items,
    string PaymentType,     // "Cash" | "Card"
    decimal PaymentAmount);

public sealed record SaleItemRequest(
    string Barcode,
    decimal Quantity);

// ── Response DTOs ──────────────────────────────────────────────────────────

public sealed record ShiftDto(
    Guid ShiftId,
    Guid StoreId,
    string Status,              // Opening | Open | OpenFailed | Closing | Closed | CloseFailed
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string? ProviderShiftId,
    string FiscalStatus,        // mirror of Status for the fiscal layer
    decimal TotalSales,
    int? ShiftNumber,
    decimal? OpeningCash = null,
    // TASK-356: cash reconciliation. ClosingCash is what the cashier counted at close
    // (null until CloseShiftAsync is called with a non-null ActualClosingCash).
    // ExpectedCashAmount = OpeningCash + sum of this shift's CASH-only sales (card
    // payments never touch the physical drawer). CashDiscrepancy = ClosingCash -
    // ExpectedCashAmount: positive = surplus, negative = shortage, null = not
    // reconciled (shift still open, or closed without a cash count).
    decimal? ClosingCash = null,
    decimal? ExpectedCashAmount = null,
    decimal? CashDiscrepancy = null);

public sealed record SaleDto(
    Guid TransactionId,
    Guid ShiftId,
    IReadOnlyList<SaleItemDto> Items,
    decimal Subtotal,
    string PaymentType,
    decimal PaymentAmount,
    decimal Change,
    string FiscalStatus,    // pending_fiscalization | fiscalized
    string? FiscalNumber,
    string ReceiptNumber,
    DateTime CreatedAt);

public sealed record SaleItemDto(
    Guid ProductId,
    string ProductName,
    string Barcode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal Total);

public sealed record SalesListDto(
    IReadOnlyList<SaleDto> Items,
    decimal TotalAmount);

// ── Fiscalization retry DTOs ───────────────────────────────────────────────

/// <summary>Slim summary of a transaction awaiting fiscalization retry.</summary>
public sealed record PendingFiscalizationDto(
    Guid Id,
    Guid ShiftId,
    Guid TenantId,
    DateTime CreatedAt,
    int RetryCount,
    IReadOnlyList<SaleItemDto> Items,
    decimal Total);

/// <summary>Result of a single fiscalization attempt (for retry endpoint response).</summary>
public sealed record FiscalizeResultDto(
    bool Ok,
    string? FiscalNumber = null,
    string? Error = null);
