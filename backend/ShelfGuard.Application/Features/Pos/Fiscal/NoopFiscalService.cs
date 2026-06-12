namespace ShelfGuard.Application.Features.Pos.Fiscal;

/// <summary>
/// Fallback when no ПРРО provider is configured (PRRO__PROVIDER unset).
/// Everything resolves to pending_fiscalization so the offline-first sale flow
/// (ADR-011) keeps working; the retry job will fiscalize once a provider is wired.
/// </summary>
public sealed class NoopFiscalService : IFiscalService
{
    public Task<FiscalHealthResult> PingAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalHealthResult(
            Reachable: false,
            Provider: "none",
            FiscalNumber: null,
            IsTestRegister: null,
            HasOpenShift: null,
            Error: "No fiscal provider configured (PRRO__PROVIDER is not set)."));

    public Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(
            ProviderShiftId: null,
            Status: FiscalShiftStatus.PendingFiscalization,
            Serial: null,
            OpenedAt: null,
            ClosedAt: null));

    public Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(
            ProviderShiftId: null,
            Status: FiscalShiftStatus.PendingFiscalization,
            Serial: null,
            OpenedAt: null,
            ClosedAt: null));

    public Task<FiscalShiftResult> GetShiftStatusAsync(string providerShiftId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(
            ProviderShiftId: providerShiftId,
            Status: FiscalShiftStatus.PendingFiscalization,
            Serial: null,
            OpenedAt: null,
            ClosedAt: null));

    public Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            ProviderReceiptId: null,
            Status: FiscalReceiptStatus.PendingFiscalization,
            FiscalNumber: null,
            FiscalDate: null,
            TotalAmount: null,
            TaxUrl: null));

    public Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            ProviderReceiptId: providerReceiptId,
            Status: FiscalReceiptStatus.PendingFiscalization,
            FiscalNumber: null,
            FiscalDate: null,
            TotalAmount: null,
            TaxUrl: null));
}
