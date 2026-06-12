namespace ShelfGuard.Application.Features.Pos.Fiscal;

/// <summary>
/// Fiscalization provider abstraction (ADR-011/ADR-012). Implementations live in
/// ShelfGuard.Infrastructure/Integrations/Prro and are selected via PRRO__PROVIDER.
/// All operations may throw <see cref="FiscalProviderException"/>; callers (sale flow,
/// retry job) must treat failures as "still pending_fiscalization", never block the sale.
/// </summary>
public interface IFiscalService
{
    /// <summary>Provider reachability + cash register info. Never throws — errors land in the result.</summary>
    Task<FiscalHealthResult> PingAsync(CancellationToken ct = default);

    Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default);

    Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default);

    /// <summary>Submits a sale receipt for fiscalization.</summary>
    Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default);

    /// <summary>Polls receipt state (fiscal number arrives asynchronously from ДПС).</summary>
    Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default);
}
