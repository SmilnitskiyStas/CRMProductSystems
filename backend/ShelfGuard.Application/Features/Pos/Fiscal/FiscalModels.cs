namespace ShelfGuard.Application.Features.Pos.Fiscal;

// Provider-agnostic fiscal DTOs (ADR-012): the Application layer never sees
// Checkbox (or any other ПРРО provider) wire shapes. Money is decimal UAH;
// providers convert to their own units (Checkbox: kopecks) internally.

public enum FiscalPaymentKind
{
    Cash,
    Cashless,
}

public enum FiscalReceiptStatus
{
    /// <summary>No provider configured / not yet submitted — offline-first default (ADR-011).</summary>
    PendingFiscalization,
    Created,
    Done,
    Error,
    Cancelled,
}

public enum FiscalShiftStatus
{
    /// <summary>No provider configured — shift exists only locally.</summary>
    PendingFiscalization,
    Created,
    Opening,
    Opened,
    Closing,
    Closed,
}

/// <param name="Code">Internal product code (we use the catalog product id).</param>
/// <param name="UnitPrice">Price per unit in UAH.</param>
/// <param name="Quantity">Units sold; supports fractional (weight) quantities, e.g. 2.25 kg.</param>
public sealed record FiscalReceiptItem(
    string Code,
    string Name,
    decimal UnitPrice,
    decimal Quantity,
    string? Barcode = null);

public sealed record FiscalPayment(FiscalPaymentKind Kind, decimal Amount);

/// <param name="LocalReceiptId">Our pos_transaction id (uuid) — used as the provider-side
/// idempotency id so the retry job can re-submit safely.</param>
public sealed record FiscalReceiptRequest(
    IReadOnlyList<FiscalReceiptItem> Items,
    IReadOnlyList<FiscalPayment> Payments,
    string? LocalReceiptId = null,
    string? CashierName = null);

public sealed record FiscalReceiptResult(
    string? ProviderReceiptId,
    FiscalReceiptStatus Status,
    string? FiscalNumber,
    DateTimeOffset? FiscalDate,
    decimal? TotalAmount,
    string? TaxUrl);

public sealed record FiscalShiftResult(
    string? ProviderShiftId,
    FiscalShiftStatus Status,
    int? Serial,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt);

public sealed record FiscalHealthResult(
    bool Reachable,
    string Provider,
    string? FiscalNumber,
    bool? IsTestRegister,
    bool? HasOpenShift,
    string? Error);

/// <summary>Result of a cashier credential check (signin + profile read, no shift side effects).</summary>
public sealed record FiscalCashierResult(
    bool Ok,
    string? CashierName,
    string? Error);
