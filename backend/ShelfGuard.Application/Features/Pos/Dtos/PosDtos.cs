namespace ShelfGuard.Application.Features.Pos.Dtos;

// ── Request DTOs ───────────────────────────────────────────────────────────

public sealed record OpenShiftRequest(
    Guid StoreId,
    decimal? OpeningCash = null);

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
    int? ShiftNumber);

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
