using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>Kind of a contract-settings image asset upload.</summary>
public enum ContractAssetKind
{
    Signature,
    Stamp,
}

/// <summary>
/// Cooperation agreement lifecycle (TASK-317):
/// pending → approve (contract PDF generated) → awaiting_signature →
/// mark-signed → active → terminate → terminated; pending → reject → rejected.
/// Client side is scoped by the caller's tenant id; supplier side follows the
/// supplier-cabinet convention (caller = supplier tenant, ids never trusted
/// across tenants).
/// </summary>
public interface ISupplierAgreementService
{
    // ── Client side ───────────────────────────────────────────────────────────

    /// <summary>Client submits a cooperation request to a marketplace supplier (by public supplier id).</summary>
    Task<(CooperationAgreementDto? Agreement, string? Error, bool IsDuplicate)> SubmitRequestAsync(
        Guid clientTenantId, Guid supplierId, string? message, Guid userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CooperationAgreementDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    /// <summary>Contract PDF download — allowed for both parties of the agreement.</summary>
    Task<(byte[]? Content, string? FileName, string? Error)> DownloadContractAsync(
        Guid agreementId, Guid callerTenantId, CancellationToken ct = default);

    // ── Supplier side ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<CooperationAgreementDto>> ListForSupplierAsync(
        Guid supplierTenantId, string? status, CancellationToken ct = default);

    Task<(CooperationAgreementDto? Agreement, string? Error)> ApproveAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default);

    Task<(CooperationAgreementDto? Agreement, string? Error)> RejectAsync(
        Guid supplierTenantId, Guid agreementId, string reason, CancellationToken ct = default);

    /// <summary>Re-renders the contract PDF (same contract number). Only while awaiting_signature.</summary>
    Task<(CooperationAgreementDto? Agreement, string? Error)> RegenerateContractAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default);

    Task<(CooperationAgreementDto? Agreement, string? Error)> SendToVchasnoAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default);

    Task<(CooperationAgreementDto? Agreement, string? Error)> MarkSignedAsync(
        Guid supplierTenantId, Guid agreementId, CancellationToken ct = default);

    Task<(CooperationAgreementDto? Agreement, string? Error)> TerminateAsync(
        Guid supplierTenantId, Guid agreementId, string? reason, CancellationToken ct = default);

    // ── Contract settings (supplier requisites) ───────────────────────────────

    /// <summary>Returns null when the supplier has not filled requisites yet (controller → 404).</summary>
    Task<SupplierContractSettingsDto?> GetContractSettingsAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    Task<(SupplierContractSettingsDto? Settings, string? Error)> UpsertContractSettingsAsync(
        Guid supplierTenantId, UpsertContractSettingsDto request, CancellationToken ct = default);

    /// <summary>Saves a signature/stamp image under wwwroot and stores its URL in the settings.</summary>
    Task<(string? Url, string? Error)> UploadContractAssetAsync(
        Guid supplierTenantId, Stream stream, string fileName, ContractAssetKind kind,
        CancellationToken ct = default);
}
