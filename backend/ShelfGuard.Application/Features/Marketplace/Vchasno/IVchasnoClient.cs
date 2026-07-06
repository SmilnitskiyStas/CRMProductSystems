namespace ShelfGuard.Application.Features.Marketplace.Vchasno;

/// <summary>
/// Minimal client for the Вчасно (vchasno.ua) e-document API used to send
/// cooperation contracts for electronic signature (TASK-317).
/// Implementation is isolated in ShelfGuard.Infrastructure/Integrations/Vchasno
/// — mirrors the IFiscalService / Checkbox pattern (ADR-013).
/// </summary>
public interface IVchasnoClient
{
    /// <summary>Uploads a PDF document and returns the Вчасно document id.</summary>
    Task<string> UploadDocumentAsync(
        string fileName, byte[] pdfBytes, string? recipientEdrpou, string title,
        CancellationToken ct = default);

    /// <summary>Returns the raw document status string, or null when the document is not found.</summary>
    Task<string?> GetDocumentStatusAsync(string documentId, CancellationToken ct = default);
}

/// <summary>
/// Per-tenant IVchasnoClient resolution from integration_configs
/// (service = "vchasno", config: { "api_key": "..." }) — same model as
/// IFiscalServiceFactory (ADR-013).
/// </summary>
public interface IVchasnoClientFactory
{
    /// <summary>Returns null when the tenant has no enabled "vchasno" integration config.</summary>
    Task<IVchasnoClient?> GetForTenantAsync(Guid tenantId, CancellationToken ct = default);
}
