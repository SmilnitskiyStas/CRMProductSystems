using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Cooperation agreement between a supplier tenant and a client tenant
/// (TASK-316). The client submits a request (pending); the supplier approves
/// or rejects it; on approval a contract PDF is generated and signed
/// (awaiting_signature → active). Only clients with an active agreement can
/// place marketplace orders. At most one live agreement per
/// (SupplierTenantId, ClientTenantId) pair — enforced by a partial unique
/// index excluding rejected/terminated rows.
/// </summary>
public sealed class SupplierAgreement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierTenantId { get; set; }
    public Guid ClientTenantId { get; set; }
    /// <summary>See <see cref="SupplierAgreementStatus"/>.</summary>
    public string Status { get; set; } = SupplierAgreementStatus.Pending;

    /// <summary>Optional message from the client accompanying the request.</summary>
    public string? RequestMessage { get; set; }
    /// <summary>Filled by the supplier when Status = rejected.</summary>
    public string? RejectionReason { get; set; }

    public string? ContractNumber { get; set; }
    /// <summary>Server-side path of the generated contract PDF.</summary>
    public string? ContractFilePath { get; set; }
    /// <summary>Document id in the Vchasno e-signature service, if sent there.</summary>
    public string? VchasnoDocumentId { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }

    /// <summary>Client-side user who submitted the request.</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
